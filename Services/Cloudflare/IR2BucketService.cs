using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;

namespace DropAndForget.Services.Cloudflare;

/// <summary>
/// Provides bucket operations against Cloudflare R2.
/// </summary>
public interface IR2BucketService
{
    /// <summary>
    /// Lists every object in the bucket.
    /// </summary>
    Task<IReadOnlyList<R2ObjectInfo>> ListAllObjectsAsync(AppConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads metadata for one object.
    /// </summary>
    Task<R2ObjectInfo?> HeadObjectAsync(AppConfig config, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a folder as a zip archive.
    /// </summary>
    Task DownloadFolderAsZipAsync(AppConfig config, BucketItem item, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads one object into a stream.
    /// </summary>
    Task DownloadFileAsync(AppConfig config, string objectKey, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads one object directly to a file.
    /// </summary>
    Task DownloadObjectToFileAsync(AppConfig config, string objectKey, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists bucket items under a prefix.
    /// </summary>
    Task<IReadOnlyList<BucketItem>> ListAsync(AppConfig config, string? prefix = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches bucket items by term.
    /// </summary>
    Task<IReadOnlyList<BucketItem>> SearchAsync(AppConfig config, string term, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one item or folder tree.
    /// </summary>
    Task<int> DeleteAsync(AppConfig config, BucketItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one object by exact key.
    /// </summary>
    Task DeleteObjectByKeyAsync(AppConfig config, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a folder marker object.
    /// </summary>
    Task<string> CreateFolderAsync(AppConfig config, string folderName, string? prefix = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames one item or folder tree.
    /// </summary>
    Task<int> RenameAsync(AppConfig config, BucketItem item, string newDisplayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves one item or folder tree into another folder.
    /// </summary>
    Task<int> MoveAsync(AppConfig config, BucketItem item, string targetFolderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads one file from disk.
    /// </summary>
    Task<string> UploadFileAsync(AppConfig config, string filePath, string? prefix = null, string? relativeObjectPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a byte payload.
    /// </summary>
    Task UploadBytesAsync(AppConfig config, string objectKey, byte[] bytes, string? contentType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an object as bytes.
    /// </summary>
    Task<byte[]> DownloadBytesAsync(AppConfig config, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an object as UTF-8 text.
    /// </summary>
    Task<string> DownloadTextAsync(AppConfig config, string objectKey, CancellationToken cancellationToken = default);
}
