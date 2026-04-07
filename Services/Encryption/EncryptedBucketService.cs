using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;
using DropAndForget.Serialization;
using DropAndForget.Services.Cloudflare;
using NSec.Cryptography;

namespace DropAndForget.Services.Encryption;

/// <summary>
/// Stores encrypted file contents and metadata in R2.
/// </summary>
public sealed class EncryptedBucketService(IR2BucketService bucketService) : IEncryptedBucketService
{
    private const string ManifestKey = ".daf/crypto.json";
    private const string IndexKey = ".daf/index.enc";
    private const string ObjectsPrefix = "objects/";
    private const int FormatVersion = 1;
    private const string BucketMasterContext = "bucket-master";
    private const string UnlockCheckContext = "unlock-check";
    private const string UnlockCheckValue = "drop-and-forget-unlock";
    private const string FilePayloadContext = "file-payload";
    private const string FileKeyContext = "file-key";
    private const string ApplicationJson = "application/json";
    private const string ApplicationOctetStream = "application/octet-stream";
    private static readonly AeadAlgorithm EncryptionAlgorithm = AeadAlgorithm.XChaCha20Poly1305;
    private static readonly Argon2Parameters DefaultArgon2Parameters = new()
    {
        MemorySize = 65536,
        NumberOfPasses = 3,
        DegreeOfParallelism = 1
    };

    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private readonly IR2BucketService _bucketService = bucketService;
    private byte[]? _bucketMasterKey;
    private EncryptedIndex? _index;
    private string _indexETag = string.Empty;

    /// <inheritdoc/>
    public bool IsUnlocked => _bucketMasterKey is not null && _index is not null;

    /// <inheritdoc/>
    public async Task<EncryptedBucketRemoteState> GetRemoteStateAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        var manifest = await _bucketService.HeadObjectAsync(config, ManifestKey, cancellationToken);
        var index = await _bucketService.HeadObjectAsync(config, IndexKey, cancellationToken);
        return manifest is not null || index is not null
            ? EncryptedBucketRemoteState.Encrypted
            : EncryptedBucketRemoteState.Plain;
    }

    /// <inheritdoc/>
    public bool RequiresUnlock(AppConfig config)
    {
        return config.IsEncryptionEnabled && config.EncryptionBootstrapCompleted;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(AppConfig config, string passphrase, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        await _sessionLock.WaitAsync(cancellationToken);

        try
        {
            var existingObjects = await _bucketService.ListAllObjectsAsync(config, cancellationToken);
            if (existingObjects.Count > 0)
            {
                throw new InvalidOperationException("Encryption setup needs an empty bucket.");
            }

            var masterKey = RandomNumberGenerator.GetBytes(EncryptionAlgorithm.KeySize);
            var salt = RandomNumberGenerator.GetBytes(16);
            var kek = EncryptedBucketCrypto.DeriveKeyEncryptionKey(passphrase, salt, DefaultArgon2Parameters, EncryptionAlgorithm);
            var now = DateTime.UtcNow;

            var manifest = CreateManifest(kek, masterKey, salt, now, createdAtUtc: now);

            var index = new EncryptedIndex { UpdatedAtUtc = now };
            await WriteManifestAsync(config, manifest, cancellationToken);
            await WriteIndexAsync(config, masterKey, index, expectedEtag: null, cancellationToken);

            _bucketMasterKey = masterKey;
            _index = index;
            _indexETag = (await _bucketService.HeadObjectAsync(config, IndexKey, cancellationToken))?.ETag ?? string.Empty;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task UnlockAsync(AppConfig config, string passphrase, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        await _sessionLock.WaitAsync(cancellationToken);

        try
        {
            var manifest = await ReadManifestAsync(config, cancellationToken);
            var salt = Convert.FromBase64String(manifest.Salt);
            var argon2 = BuildArgon2Parameters(manifest);
            var kek = EncryptedBucketCrypto.DeriveKeyEncryptionKey(passphrase, salt, argon2, EncryptionAlgorithm);
            var masterKey = EncryptedBucketCrypto.UnwrapKey(kek, Convert.FromBase64String(manifest.WrappedBucketMasterKey), BucketMasterContext, EncryptionAlgorithm, AppJsonSerializerContext.Default.Options, FormatVersion);
            var verification = EncryptedBucketCrypto.DecryptBytes(kek, Convert.FromBase64String(manifest.VerificationBlob), UnlockCheckContext, EncryptionAlgorithm, AppJsonSerializerContext.Default.Options, FormatVersion);
            var verificationText = Encoding.UTF8.GetString(verification);

            if (!string.Equals(verificationText, UnlockCheckValue, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Wrong passphrase.");
            }

            var (index, eTag) = await ReadIndexAsync(config, masterKey, cancellationToken);
            _bucketMasterKey = masterKey;
            _index = index;
            _indexETag = eTag;
        }
        catch (FormatException)
        {
            throw HandleUnlockFailure();
        }
        catch (InvalidOperationException)
        {
            Lock();
            throw;
        }
        catch (CryptographicException)
        {
            throw HandleUnlockFailure();
        }
        catch (JsonException)
        {
            throw HandleUnlockFailure();
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <inheritdoc/>
    public void Lock()
    {
        if (_bucketMasterKey is not null)
        {
            CryptographicOperations.ZeroMemory(_bucketMasterKey);
        }

        _bucketMasterKey = null;
        _index = null;
        _indexETag = string.Empty;
    }

    /// <inheritdoc/>
    public async Task DeleteBucketAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        Lock();
        var objects = await _bucketService.ListAllObjectsAsync(config, cancellationToken);
        foreach (var obj in objects)
        {
            await _bucketService.DeleteObjectByKeyAsync(config, obj.Key, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task ChangePassphraseAsync(AppConfig config, string currentPassphrase, string nextPassphrase, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nextPassphrase);

        await _sessionLock.WaitAsync(cancellationToken);

        try
        {
            if (!IsUnlocked)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(currentPassphrase);
                var manifest = await ReadManifestAsync(config, cancellationToken);
                var salt = Convert.FromBase64String(manifest.Salt);
                var currentKek = EncryptedBucketCrypto.DeriveKeyEncryptionKey(currentPassphrase, salt, BuildArgon2Parameters(manifest), EncryptionAlgorithm);
                _bucketMasterKey = EncryptedBucketCrypto.UnwrapKey(currentKek, Convert.FromBase64String(manifest.WrappedBucketMasterKey), BucketMasterContext, EncryptionAlgorithm, AppJsonSerializerContext.Default.Options, FormatVersion);
                (_index, _indexETag) = await ReadIndexAsync(config, GetUnlockedMasterKey(), cancellationToken);
            }

            var currentManifest = await ReadManifestAsync(config, cancellationToken);
            var nextSalt = RandomNumberGenerator.GetBytes(16);
            var nextKek = EncryptedBucketCrypto.DeriveKeyEncryptionKey(nextPassphrase, nextSalt, DefaultArgon2Parameters, EncryptionAlgorithm);
            var updated = CreateManifest(nextKek, GetUnlockedMasterKey(), nextSalt, DateTime.UtcNow, currentManifest.CreatedAtUtc);

            await WriteManifestAsync(config, updated, cancellationToken);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BucketItem>> ListAsync(AppConfig config, string? prefix = null, CancellationToken cancellationToken = default)
    {
        await EnsureUnlockedAsync(config, cancellationToken);
        var normalizedPrefix = EncryptedBucketPathHelper.NormalizePrefix(prefix);
        var entries = EncryptedBucketIndexView.GetVisibleEntries(GetIndex(), normalizedPrefix);
        return entries.Select(EncryptedBucketIndexView.CreateBucketItem).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BucketItem>> SearchAsync(AppConfig config, string term, CancellationToken cancellationToken = default)
    {
        await EnsureUnlockedAsync(config, cancellationToken);
        var normalized = term.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        return GetIndex().Entries
            .Where(entry => entry.RelativePath.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || entry.DisplayName.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.IsFolder)
            .ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(EncryptedBucketIndexView.CreateBucketItem)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<string> UploadFileAsync(AppConfig config, string filePath, string? prefix = null, string? relativeObjectPath = null, CancellationToken cancellationToken = default)
    {
        await EnsureUnlockedAsync(config, cancellationToken);

        var relativePath = EncryptedBucketPathHelper.BuildRelativePath(filePath, prefix, relativeObjectPath);
        if (GetEntry(relativePath) is not null)
        {
            throw new InvalidOperationException($"{relativePath} already exists.");
        }

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var fileKey = RandomNumberGenerator.GetBytes(EncryptionAlgorithm.KeySize);
        var encryptedPayload = EncryptedBucketCrypto.EncryptBytes(fileKey, bytes, FilePayloadContext, EncryptionAlgorithm, AppJsonSerializerContext.Default.Options, FormatVersion);
        var objectId = Guid.NewGuid().ToString("N");
        var objectKey = ObjectsPrefix + objectId;

        await _bucketService.UploadBytesAsync(config, objectKey, encryptedPayload, ApplicationOctetStream, cancellationToken);

        var entry = new EncryptedIndexEntry
        {
            RelativePath = relativePath,
            DisplayName = EncryptedBucketPathHelper.GetName(relativePath),
            IsFolder = false,
            ObjectId = objectId,
            WrappedFileKey = Convert.ToBase64String(EncryptedBucketCrypto.WrapKey(GetUnlockedMasterKey(), fileKey, FileKeyContext, EncryptionAlgorithm, AppJsonSerializerContext.Default.Options, FormatVersion)),
            Size = bytes.LongLength,
            ContentType = EncryptedBucketIndexView.GuessContentType(relativePath),
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow
        };

        var index = EncryptedBucketIndexView.CloneIndex(GetIndex());
        index.Entries.Add(entry);

        try
        {
            await WriteIndexAsync(config, GetUnlockedMasterKey(), index, _indexETag, cancellationToken);
            _index = index;
            return relativePath;
        }
        catch (Exception ex) when (ShouldDeleteUploadedObject(ex))
        {
            await _bucketService.DeleteObjectByKeyAsync(config, objectKey, cancellationToken);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DownloadFileAsync(AppConfig config, string relativePath, Stream destination, CancellationToken cancellationToken = default)
    {
        var bytes = await DownloadBytesAsync(config, relativePath, cancellationToken);
        await destination.WriteAsync(bytes, cancellationToken);
        if (destination.CanSeek)
        {
            destination.Position = 0;
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> DownloadBytesAsync(AppConfig config, string relativePath, CancellationToken cancellationToken = default)
    {
        await EnsureUnlockedAsync(config, cancellationToken);
        var entry = GetEntry(relativePath);
        if (entry is null || entry.IsFolder)
        {
            throw new InvalidOperationException("Encrypted file missing from index.");
        }

        var fileKey = EncryptedBucketCrypto.UnwrapKey(GetUnlockedMasterKey(), Convert.FromBase64String(entry.WrappedFileKey), FileKeyContext, EncryptionAlgorithm, AppJsonSerializerContext.Default.Options, FormatVersion);
        var payload = await _bucketService.DownloadBytesAsync(config, ObjectsPrefix + entry.ObjectId, cancellationToken);
        return EncryptedBucketCrypto.DecryptBytes(fileKey, payload, FilePayloadContext, EncryptionAlgorithm, AppJsonSerializerContext.Default.Options, FormatVersion);
    }

    /// <inheritdoc/>
    public async Task DownloadFolderAsZipAsync(AppConfig config, BucketItem item, Stream destination, CancellationToken cancellationToken = default)
    {
        await EnsureUnlockedAsync(config, cancellationToken);
        var prefix = EncryptedBucketPathHelper.NormalizePrefix(item.Key);
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var entry in GetIndex().Entries.Where(entry => !entry.IsFolder && entry.RelativePath.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var relative = entry.RelativePath[prefix.Length..];
            if (string.IsNullOrEmpty(relative))
            {
                continue;
            }

            var zipEntry = archive.CreateEntry(item.DisplayName.TrimEnd('/') + "/" + relative, CompressionLevel.Optimal);
            await using var zipStream = zipEntry.Open();
            var bytes = await DownloadBytesAsync(config, entry.RelativePath, cancellationToken);
            await zipStream.WriteAsync(bytes, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<string> CreateFolderAsync(AppConfig config, string folderName, string? prefix = null, CancellationToken cancellationToken = default)
    {
        await EnsureUnlockedAsync(config, cancellationToken);
        var relativePath = EncryptedBucketPathHelper.CombineRelative(prefix, folderName);
        if (GetEntry(relativePath) is not null)
        {
            throw new InvalidOperationException($"{folderName} already exists.");
        }

        var index = EncryptedBucketIndexView.CloneIndex(GetIndex());
        index.Entries.Add(new EncryptedIndexEntry
        {
            RelativePath = relativePath,
            DisplayName = EncryptedBucketPathHelper.GetName(relativePath),
            IsFolder = true,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow
        });

        await WriteIndexAsync(config, GetUnlockedMasterKey(), index, _indexETag, cancellationToken);
        _index = index;
        return EncryptedBucketPathHelper.NormalizePrefix(relativePath);
    }

    /// <inheritdoc/>
    public async Task<int> RenameAsync(AppConfig config, BucketItem item, string newDisplayName, CancellationToken cancellationToken = default)
    {
        await EnsureUnlockedAsync(config, cancellationToken);
        var oldPath = item.Key.TrimEnd('/');
        var parentPrefix = EncryptedBucketPathHelper.GetParentPrefix(oldPath);
        var newPath = string.IsNullOrEmpty(parentPrefix) ? newDisplayName.Trim() : parentPrefix + newDisplayName.Trim();
        if (GetEntry(newPath) is not null)
        {
            throw new InvalidOperationException($"{newDisplayName} already exists.");
        }

        var index = EncryptedBucketIndexView.CloneIndex(GetIndex());
        var changed = RelocateEntries(index, item, oldPath, newPath);

        if (changed == 0)
        {
            return 0;
        }

        await WriteIndexAsync(config, GetUnlockedMasterKey(), index, _indexETag, cancellationToken);
        _index = index;
        return changed;
    }

    /// <inheritdoc/>
    public async Task<int> MoveAsync(AppConfig config, BucketItem item, string targetFolderPath, CancellationToken cancellationToken = default)
    {
        await EnsureUnlockedAsync(config, cancellationToken);
        var oldPath = item.Key.TrimEnd('/');
        var newPath = EncryptedBucketPathHelper.CombineRelative(targetFolderPath, item.DisplayName);
        if (GetEntry(newPath) is not null)
        {
            throw new InvalidOperationException($"{item.DisplayName} already exists.");
        }

        var index = EncryptedBucketIndexView.CloneIndex(GetIndex());
        var changed = RelocateEntries(index, item, oldPath, newPath);

        if (changed == 0)
        {
            return 0;
        }

        await WriteIndexAsync(config, GetUnlockedMasterKey(), index, _indexETag, cancellationToken);
        _index = index;
        return changed;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteAsync(AppConfig config, BucketItem item, CancellationToken cancellationToken = default)
    {
        await EnsureUnlockedAsync(config, cancellationToken);
        var path = item.Key.TrimEnd('/');
        var index = EncryptedBucketIndexView.CloneIndex(GetIndex());
        var toDelete = index.Entries
            .Where(entry => string.Equals(entry.RelativePath, path, StringComparison.Ordinal)
                || (item.IsFolder && entry.RelativePath.StartsWith(path + "/", StringComparison.Ordinal)))
            .ToList();

        if (toDelete.Count == 0)
        {
            return 0;
        }

        foreach (var entry in toDelete.Where(entry => !entry.IsFolder && !string.IsNullOrWhiteSpace(entry.ObjectId)))
        {
            await _bucketService.DeleteObjectByKeyAsync(config, ObjectsPrefix + entry.ObjectId, cancellationToken);
        }

        index.Entries.RemoveAll(entry => toDelete.Contains(entry));
        await WriteIndexAsync(config, GetUnlockedMasterKey(), index, _indexETag, cancellationToken);
        _index = index;
        return toDelete.Count(entry => !entry.IsFolder);
    }

    private async Task EnsureUnlockedAsync(AppConfig config, CancellationToken cancellationToken)
    {
        if (!config.IsEncryptionEnabled)
        {
            throw new InvalidOperationException("Encryption mode not active.");
        }

        if (!config.EncryptionBootstrapCompleted)
        {
            throw new InvalidOperationException("Encryption setup not finished yet.");
        }

        if (!IsUnlocked)
        {
            throw new InvalidOperationException("Unlock bucket first.");
        }

        if (string.IsNullOrEmpty(_indexETag))
        {
            var (_, eTag) = await ReadIndexAsync(config, GetUnlockedMasterKey(), cancellationToken);
            _indexETag = eTag;
        }
    }

    private async Task<EncryptedBucketManifest> ReadManifestAsync(AppConfig config, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _bucketService.DownloadTextAsync(config, ManifestKey, cancellationToken);
            return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.EncryptedBucketManifest)
                ?? throw new InvalidOperationException("Corrupted encryption manifest.");
        }
        catch (Amazon.S3.AmazonS3Exception)
        {
            throw new InvalidOperationException("Encrypted mode metadata missing. Run setup again.");
        }
    }

    private async Task<(EncryptedIndex Index, string ETag)> ReadIndexAsync(AppConfig config, byte[] bucketMasterKey, CancellationToken cancellationToken)
    {
        var head = await _bucketService.HeadObjectAsync(config, IndexKey, cancellationToken);
        if (head is null)
        {
            throw new InvalidOperationException("Encrypted index missing.");
        }

        var payload = await _bucketService.DownloadBytesAsync(config, IndexKey, cancellationToken);
        var bytes = EncryptedBucketCrypto.DecryptBytes(bucketMasterKey, payload, "index", EncryptionAlgorithm, AppJsonSerializerContext.Default.Options, FormatVersion);
        var json = Encoding.UTF8.GetString(bytes);
        var index = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.EncryptedIndex)
            ?? throw new InvalidOperationException("Corrupted encrypted index.");
        return (index, head.ETag ?? string.Empty);
    }

    private async Task WriteIndexAsync(AppConfig config, byte[] bucketMasterKey, EncryptedIndex index, string? expectedEtag, CancellationToken cancellationToken)
    {
        index.UpdatedAtUtc = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(index, AppJsonSerializerContext.Default.EncryptedIndex);
        var payload = EncryptedBucketCrypto.EncryptBytes(bucketMasterKey, Encoding.UTF8.GetBytes(json), "index", EncryptionAlgorithm, AppJsonSerializerContext.Default.Options, FormatVersion);

        if (!string.IsNullOrWhiteSpace(expectedEtag))
        {
            var currentHead = await _bucketService.HeadObjectAsync(config, IndexKey, cancellationToken);
            var currentEtag = currentHead?.ETag ?? string.Empty;
            if (!string.Equals(currentEtag, expectedEtag, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Encrypted index changed on another device. Refresh, unlock again, retry.");
            }
        }

        await _bucketService.UploadBytesAsync(config, IndexKey, payload, ApplicationOctetStream, cancellationToken);
        _indexETag = (await _bucketService.HeadObjectAsync(config, IndexKey, cancellationToken))?.ETag ?? string.Empty;
    }

    private async Task WriteManifestAsync(AppConfig config, EncryptedBucketManifest manifest, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, AppJsonSerializerContext.Default.EncryptedBucketManifest));
        await _bucketService.UploadBytesAsync(config, ManifestKey, bytes, ApplicationJson, cancellationToken);
    }

    private static EncryptedBucketManifest CreateManifest(byte[] kek, byte[] bucketMasterKey, byte[] salt, DateTime updatedAtUtc, DateTime createdAtUtc)
    {
        return new EncryptedBucketManifest
        {
            Argon2MemorySize = DefaultArgon2Parameters.MemorySize,
            Argon2Iterations = DefaultArgon2Parameters.NumberOfPasses,
            Argon2Parallelism = DefaultArgon2Parameters.DegreeOfParallelism,
            Salt = Convert.ToBase64String(salt),
            WrappedBucketMasterKey = Convert.ToBase64String(EncryptedBucketCrypto.WrapKey(kek, bucketMasterKey, BucketMasterContext, EncryptionAlgorithm, AppJsonSerializerContext.Default.Options, FormatVersion)),
            VerificationBlob = Convert.ToBase64String(EncryptedBucketCrypto.EncryptBytes(kek, Encoding.UTF8.GetBytes(UnlockCheckValue), UnlockCheckContext, EncryptionAlgorithm, AppJsonSerializerContext.Default.Options, FormatVersion)),
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc
        };
    }

    private static int RelocateEntries(EncryptedIndex index, BucketItem item, string oldPath, string newPath)
    {
        var changed = 0;
        var updatedAtUtc = DateTime.UtcNow;

        foreach (var entry in index.Entries)
        {
            if (!ShouldRelocateEntry(entry, item, oldPath))
            {
                continue;
            }

            entry.RelativePath = item.IsFolder
                ? newPath + entry.RelativePath[oldPath.Length..]
                : newPath;
            entry.DisplayName = EncryptedBucketPathHelper.GetName(entry.RelativePath);
            entry.ModifiedAtUtc = updatedAtUtc;
            changed++;
        }

        return changed;
    }

    private static bool ShouldRelocateEntry(EncryptedIndexEntry entry, BucketItem item, string oldPath)
    {
        if (item.IsFolder)
        {
            return string.Equals(entry.RelativePath, oldPath, StringComparison.Ordinal)
                || entry.RelativePath.StartsWith(oldPath + "/", StringComparison.Ordinal);
        }

        return string.Equals(entry.RelativePath, oldPath, StringComparison.Ordinal);
    }

    private static bool ShouldDeleteUploadedObject(Exception ex)
    {
        return ex is InvalidOperationException
            or Amazon.S3.AmazonS3Exception
            or IOException
            or CryptographicException
            or JsonException;
    }

    private InvalidOperationException HandleUnlockFailure()
    {
        Lock();
        return new InvalidOperationException("Wrong passphrase or corrupted encryption metadata.");
    }

    private EncryptedIndexEntry? GetEntry(string relativePath)
    {
        return GetIndex().Entries.FirstOrDefault(entry => string.Equals(entry.RelativePath, relativePath.TrimEnd('/'), StringComparison.Ordinal));
    }

    private EncryptedIndex GetIndex()
    {
        return _index ?? throw new InvalidOperationException("Unlock bucket first.");
    }

    private byte[] GetUnlockedMasterKey()
    {
        return _bucketMasterKey ?? throw new InvalidOperationException("Unlock bucket first.");
    }

    private static Argon2Parameters BuildArgon2Parameters(EncryptedBucketManifest manifest)
    {
        return new Argon2Parameters
        {
            MemorySize = manifest.Argon2MemorySize,
            NumberOfPasses = manifest.Argon2Iterations,
            DegreeOfParallelism = manifest.Argon2Parallelism
        };
    }

}
