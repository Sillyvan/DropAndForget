using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;

namespace DropAndForget.Services.Sync;

/// <summary>
/// Exposes local filesystem operations for sync mode browsing.
/// </summary>
public interface ILocalSyncBrowser
{
    /// <summary>
    /// Lists items under a prefix.
    /// </summary>
    IReadOnlyList<BucketItem> ListItems(AppConfig config, string prefix);

    /// <summary>
    /// Searches local items.
    /// </summary>
    IReadOnlyList<BucketItem> SearchItems(AppConfig config, string term);

    /// <summary>
    /// Creates a folder.
    /// </summary>
    void CreateFolder(AppConfig config, string currentPrefix, string folderName);

    /// <summary>
    /// Renames an item.
    /// </summary>
    void RenameItem(AppConfig config, BucketItem item, string newName);

    /// <summary>
    /// Moves an item into another folder.
    /// </summary>
    void MoveItem(AppConfig config, BucketItem item, string targetFolderPath);

    /// <summary>
    /// Deletes an item.
    /// </summary>
    int DeleteItem(AppConfig config, BucketItem item);

    /// <summary>
    /// Copies a file to a stream.
    /// </summary>
    Task CopyFileToAsync(AppConfig config, string relativePath, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a file into memory.
    /// </summary>
    Task<byte[]> ReadAllBytesAsync(AppConfig config, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves one item path on disk.
    /// </summary>
    string GetItemPath(AppConfig config, string relativePath);
}
