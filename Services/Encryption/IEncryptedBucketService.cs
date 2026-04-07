using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;

namespace DropAndForget.Services.Encryption;

/// <summary>
/// Provides encrypted bucket operations backed by R2.
/// </summary>
public interface IEncryptedBucketService
{
    /// <summary>
    /// Gets whether bucket metadata is unlocked in memory.
    /// </summary>
    bool IsUnlocked { get; }

    /// <summary>
    /// Gets whether the config requires an unlock step.
    /// </summary>
    bool RequiresUnlock(AppConfig config);

    /// <summary>
    /// Detects whether the remote bucket is already encrypted.
    /// </summary>
    Task<EncryptedBucketRemoteState> GetRemoteStateAsync(AppConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes encrypted bucket metadata.
    /// </summary>
    Task InitializeAsync(AppConfig config, string passphrase, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlocks an encrypted bucket.
    /// </summary>
    Task UnlockAsync(AppConfig config, string passphrase, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears unlocked key material from memory.
    /// </summary>
    void Lock();

    /// <summary>
    /// Deletes every object from an encrypted bucket.
    /// </summary>
    Task DeleteBucketAsync(AppConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewraps the bucket master key with a new passphrase.
    /// </summary>
    Task ChangePassphraseAsync(AppConfig config, string currentPassphrase, string nextPassphrase, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists decrypted items under a prefix.
    /// </summary>
    Task<IReadOnlyList<BucketItem>> ListAsync(AppConfig config, string? prefix = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches decrypted items by term.
    /// </summary>
    Task<IReadOnlyList<BucketItem>> SearchAsync(AppConfig config, string term, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encrypts and uploads a file.
    /// </summary>
    Task<string> UploadFileAsync(AppConfig config, string filePath, string? prefix = null, string? relativeObjectPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads and decrypts a file to a stream.
    /// </summary>
    Task DownloadFileAsync(AppConfig config, string relativePath, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads and decrypts a file to memory.
    /// </summary>
    Task<byte[]> DownloadBytesAsync(AppConfig config, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads and decrypts a folder as zip.
    /// </summary>
    Task DownloadFolderAsZipAsync(AppConfig config, BucketItem item, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an encrypted folder entry.
    /// </summary>
    Task<string> CreateFolderAsync(AppConfig config, string folderName, string? prefix = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames an encrypted item or folder tree.
    /// </summary>
    Task<int> RenameAsync(AppConfig config, BucketItem item, string newDisplayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an encrypted item or folder tree into another folder.
    /// </summary>
    Task<int> MoveAsync(AppConfig config, BucketItem item, string targetFolderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an encrypted item or folder tree.
    /// </summary>
    Task<int> DeleteAsync(AppConfig config, BucketItem item, CancellationToken cancellationToken = default);
}
