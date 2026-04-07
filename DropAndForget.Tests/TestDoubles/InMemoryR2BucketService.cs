using System.IO.Compression;
using System.Text;
using Amazon.S3;
using DropAndForget.Models;
using DropAndForget.Services.Cloudflare;

namespace DropAndForget.Tests.TestDoubles;

internal sealed class InMemoryR2BucketService : IR2BucketService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, StoredObject> _objects = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<R2ObjectInfo>> ListAllObjectsAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<R2ObjectInfo>>(_objects
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => pair.Value.ToObjectInfo(pair.Key))
                .ToList());
        }
    }

    public Task<R2ObjectInfo?> HeadObjectAsync(AppConfig config, string objectKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_objects.TryGetValue(objectKey, out var obj)
                ? obj.ToObjectInfo(objectKey)
                : null);
        }
    }

    public async Task DownloadFolderAsZipAsync(AppConfig config, BucketItem item, Stream destination, CancellationToken cancellationToken = default)
    {
        var prefix = item.Key.TrimEnd('/') + "/";
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var pair in Snapshot().Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal) && !pair.Key.EndsWith("/", StringComparison.Ordinal)))
        {
            var relativePath = pair.Key[prefix.Length..];
            var entry = archive.CreateEntry(item.DisplayName + "/" + relativePath);
            await using var entryStream = entry.Open();
            await entryStream.WriteAsync(pair.Value.Bytes, cancellationToken);
        }
    }

    public async Task DownloadFileAsync(AppConfig config, string objectKey, Stream destination, CancellationToken cancellationToken = default)
    {
        var bytes = await DownloadBytesAsync(config, objectKey, cancellationToken);
        await destination.WriteAsync(bytes, cancellationToken);
        if (destination.CanSeek)
        {
            destination.Position = 0;
        }
    }

    public async Task DownloadObjectToFileAsync(AppConfig config, string objectKey, string filePath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = await DownloadBytesAsync(config, objectKey, cancellationToken);
        await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
    }

    public Task<IReadOnlyList<BucketItem>> ListAsync(AppConfig config, string? prefix = null, CancellationToken cancellationToken = default)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        var folders = new Dictionary<string, BucketItem>(StringComparer.Ordinal);
        var files = new List<BucketItem>();

        foreach (var pair in Snapshot())
        {
            if (!pair.Key.StartsWith(normalizedPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var relative = pair.Key[normalizedPrefix.Length..];
            if (string.IsNullOrEmpty(relative))
            {
                continue;
            }

            var slashIndex = relative.IndexOf('/');
            if (slashIndex >= 0)
            {
                var folderName = relative[..slashIndex];
                var folderKey = normalizedPrefix + folderName;
                folders.TryAdd(folderKey, CreateFolderItem(folderKey));
                continue;
            }

            if (!pair.Key.EndsWith("/", StringComparison.Ordinal))
            {
                files.Add(CreateFileItem(pair.Key, pair.Value));
            }
        }

        var items = folders.Values
            .Concat(files)
            .OrderByDescending(item => item.IsFolder)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<BucketItem>>(items);
    }

    public Task<IReadOnlyList<BucketItem>> SearchAsync(AppConfig config, string term, CancellationToken cancellationToken = default)
    {
        var normalized = term.Trim();
        var items = Snapshot()
            .Where(pair => pair.Key.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key.EndsWith("/", StringComparison.Ordinal) ? CreateFolderItem(pair.Key.TrimEnd('/')) : CreateFileItem(pair.Key, pair.Value))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<BucketItem>>(items);
    }

    public Task<int> DeleteAsync(AppConfig config, BucketItem item, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!item.IsFolder)
            {
                return Task.FromResult(_objects.Remove(item.Key) ? 1 : 0);
            }

            var prefix = item.Key.TrimEnd('/') + "/";
            var keys = _objects.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var key in keys)
            {
                _objects.Remove(key);
            }

            return Task.FromResult(keys.Count(key => !key.EndsWith("/", StringComparison.Ordinal)));
        }
    }

    public Task DeleteObjectByKeyAsync(AppConfig config, string objectKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _objects.Remove(objectKey);
        }

        return Task.CompletedTask;
    }

    public Task<string> CreateFolderAsync(AppConfig config, string folderName, string? prefix = null, CancellationToken cancellationToken = default)
    {
        var key = NormalizePrefix(prefix) + folderName.Trim().TrimEnd('/') + "/";
        PutObject(key, []);
        return Task.FromResult(key);
    }

    public Task<int> RenameAsync(AppConfig config, BucketItem item, string newDisplayName, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!item.IsFolder)
            {
                if (!_objects.Remove(item.Key, out var obj))
                {
                    return Task.FromResult(0);
                }

                var newKey = CombineParent(item.Key, newDisplayName);
                _objects[newKey] = obj.WithNewEtag();
                return Task.FromResult(1);
            }

            var oldPrefix = item.Key.TrimEnd('/') + "/";
            var newPrefix = CombineParent(item.Key, newDisplayName).TrimEnd('/') + "/";
            var pairs = _objects.Where(pair => pair.Key.StartsWith(oldPrefix, StringComparison.Ordinal)).ToList();
            foreach (var pair in pairs)
            {
                _objects.Remove(pair.Key);
                _objects[newPrefix + pair.Key[oldPrefix.Length..]] = pair.Value.WithNewEtag();
            }

            return Task.FromResult(pairs.Count(pair => !pair.Key.EndsWith("/", StringComparison.Ordinal)));
        }
    }

    public Task<int> MoveAsync(AppConfig config, BucketItem item, string targetFolderPath, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!item.IsFolder)
            {
                if (!_objects.Remove(item.Key, out var obj))
                {
                    return Task.FromResult(0);
                }

                var newKey = NormalizePrefix(targetFolderPath) + item.DisplayName;
                _objects[newKey] = obj.WithNewEtag();
                return Task.FromResult(1);
            }

            var oldPrefix = item.Key.TrimEnd('/') + "/";
            var newPrefix = NormalizePrefix(targetFolderPath) + item.DisplayName.TrimEnd('/') + "/";
            var pairs = _objects.Where(pair => pair.Key.StartsWith(oldPrefix, StringComparison.Ordinal)).ToList();
            foreach (var pair in pairs)
            {
                _objects.Remove(pair.Key);
                _objects[newPrefix + pair.Key[oldPrefix.Length..]] = pair.Value.WithNewEtag();
            }

            return Task.FromResult(pairs.Count(pair => !pair.Key.EndsWith("/", StringComparison.Ordinal)));
        }
    }

    public async Task<string> UploadFileAsync(AppConfig config, string filePath, string? prefix = null, string? relativeObjectPath = null, CancellationToken cancellationToken = default)
    {
        var key = R2BucketPathHelper.BuildObjectKey(filePath, prefix, relativeObjectPath);
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        PutObject(key, bytes);
        return key;
    }

    public Task UploadBytesAsync(AppConfig config, string objectKey, byte[] bytes, string? contentType = null, CancellationToken cancellationToken = default)
    {
        PutObject(objectKey, bytes, contentType);
        return Task.CompletedTask;
    }

    public Task<byte[]> DownloadBytesAsync(AppConfig config, string objectKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_objects.TryGetValue(objectKey, out var obj))
            {
                throw new AmazonS3Exception($"Missing object '{objectKey}'.");
            }

            return Task.FromResult(obj.Bytes.ToArray());
        }
    }

    public async Task<string> DownloadTextAsync(AppConfig config, string objectKey, CancellationToken cancellationToken = default)
    {
        var bytes = await DownloadBytesAsync(config, objectKey, cancellationToken);
        return Encoding.UTF8.GetString(bytes);
    }

    public void PutObject(string objectKey, byte[] bytes, string? contentType = null)
    {
        lock (_gate)
        {
            _objects[objectKey] = new StoredObject(bytes.ToArray(), contentType, DateTime.UtcNow, Guid.NewGuid().ToString("N"));
        }
    }

    public bool ContainsObject(string objectKey)
    {
        lock (_gate)
        {
            return _objects.ContainsKey(objectKey);
        }
    }

    public byte[] ReadObject(string objectKey)
    {
        lock (_gate)
        {
            return _objects[objectKey].Bytes.ToArray();
        }
    }

    public IReadOnlyCollection<string> GetObjectKeys()
    {
        lock (_gate)
        {
            return _objects.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToList();
        }
    }

    private IReadOnlyList<KeyValuePair<string, StoredObject>> Snapshot()
    {
        lock (_gate)
        {
            return _objects.ToList();
        }
    }

    private static string NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        return prefix.Replace('\\', '/').Trim('/') + "/";
    }

    private static string CombineParent(string key, string newDisplayName)
    {
        var trimmedKey = key.TrimEnd('/');
        var slashIndex = trimmedKey.LastIndexOf('/');
        var parent = slashIndex >= 0 ? trimmedKey[..(slashIndex + 1)] : string.Empty;
        return parent + newDisplayName.Trim();
    }

    private static BucketItem CreateFolderItem(string key)
    {
        var normalizedKey = key.TrimEnd('/');
        return new BucketItem
        {
            Key = normalizedKey,
            DisplayName = normalizedKey[(normalizedKey.LastIndexOf('/') + 1)..],
            FolderPath = normalizedKey.Contains('/') ? normalizedKey[..normalizedKey.LastIndexOf('/')] : string.Empty,
            IsFolder = true,
            Detail = "folder",
            SizeText = "--"
        };
    }

    private static BucketItem CreateFileItem(string key, StoredObject obj)
    {
        return new BucketItem
        {
            Key = key,
            DisplayName = Path.GetFileName(key),
            FolderPath = key.Contains('/') ? key[..key.LastIndexOf('/')] : string.Empty,
            IsFolder = false,
            SizeBytes = obj.Bytes.Length,
            SizeText = obj.Bytes.Length.ToString(),
            ModifiedAt = obj.LastModifiedUtc,
            ModifiedText = obj.LastModifiedUtc.ToString("yyyy-MM-dd HH:mm")
        };
    }

    private sealed record StoredObject(byte[] Bytes, string? ContentType, DateTime LastModifiedUtc, string ETag)
    {
        public R2ObjectInfo ToObjectInfo(string key)
        {
            return new R2ObjectInfo
            {
                Key = key,
                IsFolder = key.EndsWith("/", StringComparison.Ordinal),
                Size = Bytes.LongLength,
                LastModifiedUtc = LastModifiedUtc,
                ETag = ETag
            };
        }

        public StoredObject WithNewEtag()
        {
            return this with { LastModifiedUtc = DateTime.UtcNow, ETag = Guid.NewGuid().ToString("N") };
        }
    }
}
