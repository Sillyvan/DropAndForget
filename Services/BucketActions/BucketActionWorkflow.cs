using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;
using DropAndForget.Services.BucketContent;
using DropAndForget.Services.Sync;
using DropAndForget.ViewModels;

namespace DropAndForget.Services.BucketActions;

public sealed class BucketActionWorkflow(BucketContentService bucketContentService, IStorageModeCoordinator storageModeCoordinator)
{
    private readonly BucketContentService _bucketContentService = bucketContentService;
    private readonly IStorageModeCoordinator _storageModeCoordinator = storageModeCoordinator;

    public async Task UploadDroppedFilesAsync(AppConfig config, IReadOnlyList<string> paths, string currentPrefix, bool isSyncMode, bool isEncryptionEnabled, Action<string> setStatus, CancellationToken cancellationToken = default)
    {
        var dropped = new List<string>(paths.Where(path => !string.IsNullOrWhiteSpace(path)));
        if (dropped.Count == 0)
        {
            return;
        }

        var filesToUpload = BucketUiHelpers.ExpandDroppedPaths(dropped);
        if (filesToUpload.Count == 0)
        {
            throw new InvalidOperationException("Nothing to upload.");
        }

        if (isSyncMode)
        {
            setStatus($"Copying {filesToUpload.Count} item(s) into sync folder...");
            await _storageModeCoordinator.CopyIntoSyncFolderAsync(config, dropped, currentPrefix, cancellationToken);
            setStatus($"Copied {filesToUpload.Count} item(s) into sync folder.");
            return;
        }

        foreach (var upload in filesToUpload)
        {
            setStatus(isEncryptionEnabled
                ? $"Encrypting {upload.DisplayName}..."
                : $"Uploading {upload.DisplayName}...");
            var objectKey = await _bucketContentService.UploadFileAsync(config, upload.FilePath, currentPrefix, upload.RelativeObjectPath, cancellationToken);
            setStatus(isEncryptionEnabled
                ? $"Encrypted and uploaded {upload.DisplayName}."
                : $"Uploaded {upload.DisplayName} to {objectKey}.");
        }
    }

    public Task CreateFolderAsync(AppConfig config, string folderName, IReadOnlyCollection<BucketItem> visibleItems, string currentPrefix, CancellationToken cancellationToken = default)
    {
        folderName = NormalizeValidatedFolderName(folderName, visibleItems, null);
        return _bucketContentService.CreateFolderAsync(config, folderName, currentPrefix, cancellationToken);
    }

    public Task RenameAsync(AppConfig config, BucketItem item, string newName, IReadOnlyCollection<BucketItem> visibleItems, CancellationToken cancellationToken = default)
    {
        newName = NormalizeValidatedName(newName, visibleItems, item, item.KindText);
        if (string.Equals(newName, item.DisplayName, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return _bucketContentService.RenameAsync(config, item, newName, cancellationToken);
    }

    public Task DownloadFileAsync(AppConfig config, BucketItem item, Stream destination, CancellationToken cancellationToken = default)
    {
        return _bucketContentService.DownloadFileAsync(config, item, destination, cancellationToken);
    }

    public Task DownloadFolderAsZipAsync(AppConfig config, BucketItem item, Stream destination, CancellationToken cancellationToken = default)
    {
        return _bucketContentService.DownloadFolderAsZipAsync(config, item, destination, cancellationToken);
    }

    public async Task DownloadAsZipAsync(AppConfig config, IReadOnlyList<BucketItem> items, Stream destination, CancellationToken cancellationToken = default)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            var rootName = GetUniqueZipEntryName(item.DisplayName.TrimEnd('/'), usedNames);
            if (item.IsFolder)
            {
                await AddFolderToArchiveAsync(config, item, archive, rootName, cancellationToken);
                continue;
            }

            await AddFileToArchiveAsync(config, item, archive, rootName, cancellationToken);
        }
    }

    public async Task<FilePreviewData?> LoadPreviewAsync(AppConfig config, BucketItem item, CancellationToken cancellationToken = default)
    {
        var previewKind = BucketUiHelpers.GetPreviewKind(item);
        if (previewKind is null)
        {
            throw new InvalidOperationException($"Preview not available for {item.DisplayName}.");
        }

        var bytes = await _bucketContentService.DownloadBytesAsync(config, item, cancellationToken);
        return previewKind.Value switch
        {
            FilePreviewKind.Image => new FilePreviewData(item.DisplayName, FilePreviewKind.Image, BinaryContent: bytes),
            FilePreviewKind.Pdf => new FilePreviewData(item.DisplayName, FilePreviewKind.Pdf, BinaryContent: bytes),
            FilePreviewKind.Text => new FilePreviewData(item.DisplayName, FilePreviewKind.Text, TextContent: BucketUiHelpers.ReadText(bytes)),
            _ => null
        };
    }

    public Task<int> DeleteAsync(AppConfig config, BucketItem item, CancellationToken cancellationToken = default)
    {
        return _bucketContentService.DeleteAsync(config, item, cancellationToken);
    }

    public async Task<int> DeleteAsync(AppConfig config, IReadOnlyList<BucketItem> items, CancellationToken cancellationToken = default)
    {
        var deletedCount = 0;
        foreach (var item in items)
        {
            deletedCount += await _bucketContentService.DeleteAsync(config, item, cancellationToken);
        }

        return deletedCount;
    }

    public async Task MoveAsync(AppConfig config, BucketItem item, BucketItem targetFolder, CancellationToken cancellationToken = default)
    {
        var targetFolderPath = NormalizeFolderPath(targetFolder.Key);
        EnsureMoveAllowed(item, targetFolderPath, requireFolderTarget: true);

        await EnsureNameAvailableAsync(config, targetFolderPath, item, cancellationToken);
        await _bucketContentService.MoveAsync(config, item, targetFolderPath, cancellationToken);
    }

    public async Task MoveAsync(AppConfig config, BucketItem item, string targetFolderPath, CancellationToken cancellationToken = default)
    {
        targetFolderPath = NormalizeFolderPath(targetFolderPath);
        EnsureMoveAllowed(item, targetFolderPath, requireFolderTarget: false);

        await EnsureNameAvailableAsync(config, targetFolderPath, item, cancellationToken);
        await _bucketContentService.MoveAsync(config, item, targetFolderPath, cancellationToken);
    }

    public Task MoveAsync(AppConfig config, IReadOnlyList<BucketItem> items, BucketItem targetFolder, CancellationToken cancellationToken = default)
    {
        return MoveAsync(config, items, targetFolder.Key, cancellationToken);
    }

    public async Task MoveAsync(AppConfig config, IReadOnlyList<BucketItem> items, string targetFolderPath, CancellationToken cancellationToken = default)
    {
        targetFolderPath = NormalizeFolderPath(targetFolderPath);
        var existingNames = await LoadExistingNamesAsync(config, targetFolderPath, cancellationToken);

        foreach (var item in items)
        {
            EnsureMoveAllowed(item, targetFolderPath, requireFolderTarget: false);
            if (!existingNames.Add(item.DisplayName))
            {
                throw new InvalidOperationException($"{item.KindText} {item.DisplayName} already exists.");
            }
        }

        foreach (var item in items)
        {
            await _bucketContentService.MoveAsync(config, item, targetFolderPath, cancellationToken);
        }
    }

    public Task CreatePendingFolderAsync(AppConfig config, BucketItem item, string folderName, IReadOnlyCollection<BucketItem> visibleItems, CancellationToken cancellationToken = default)
    {
        folderName = NormalizeValidatedFolderName(folderName, visibleItems, item);
        return _bucketContentService.CreateFolderAsync(config, folderName, item.FolderPath, cancellationToken);
    }

    private static string NormalizeValidatedFolderName(string folderName, IReadOnlyCollection<BucketItem> visibleItems, BucketItem? excludedItem)
    {
        return NormalizeValidatedName(folderName, visibleItems, excludedItem, "Folder");
    }

    private static void EnsureMoveAllowed(BucketItem item, string targetFolderPath, bool requireFolderTarget)
    {
        if (requireFolderTarget && string.IsNullOrEmpty(targetFolderPath))
        {
            return;
        }

        var sourcePath = NormalizeFolderPath(item.Key);
        if (string.Equals(sourcePath, targetFolderPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Can't move folder into itself.");
        }

        var sourceParentPath = NormalizeFolderPath(item.FolderPath);
        if (string.Equals(sourceParentPath, targetFolderPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{item.KindText} already in that folder.");
        }

        if (item.IsFolder && targetFolderPath.StartsWith(sourcePath + "/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Can't move folder into a child folder.");
        }
    }

    private async Task EnsureNameAvailableAsync(AppConfig config, string targetFolderPath, BucketItem item, CancellationToken cancellationToken)
    {
        var existingNames = await LoadExistingNamesAsync(config, targetFolderPath, cancellationToken);
        if (!existingNames.Add(item.DisplayName))
        {
            throw new InvalidOperationException($"{item.KindText} {item.DisplayName} already exists.");
        }
    }

    private async Task<HashSet<string>> LoadExistingNamesAsync(AppConfig config, string targetFolderPath, CancellationToken cancellationToken)
    {
        var targetItems = await _bucketContentService.ListAsync(config, targetFolderPath, cancellationToken);
        return new HashSet<string>(targetItems.Select(static item => item.DisplayName), StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeFolderPath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static string NormalizeValidatedName(string name, IReadOnlyCollection<BucketItem> visibleItems, BucketItem? excludedItem, string kindText)
    {
        name = BucketUiHelpers.NormalizeItemName(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"{kindText} name required.");
        }

        if (!BucketUiHelpers.IsValidItemName(name))
        {
            throw new InvalidOperationException($"{kindText} name can't contain / or \\.");
        }

        if (BucketUiHelpers.NameExists(visibleItems, name, excludedItem))
        {
            throw new InvalidOperationException($"{kindText} {name} already exists.");
        }

        return name;
    }

    private async Task AddFolderToArchiveAsync(AppConfig config, BucketItem folder, ZipArchive archive, string folderPath, CancellationToken cancellationToken)
    {
        var children = await _bucketContentService.ListAsync(config, folder.Key, cancellationToken);
        if (children.Count == 0)
        {
            archive.CreateEntry(folderPath.TrimEnd('/') + "/", CompressionLevel.NoCompression);
            return;
        }

        foreach (var child in children.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            var entryPath = folderPath + "/" + child.DisplayName.TrimEnd('/');
            if (child.IsFolder)
            {
                await AddFolderToArchiveAsync(config, child, archive, entryPath, cancellationToken);
                continue;
            }

            await AddFileToArchiveAsync(config, child, archive, entryPath, cancellationToken);
        }
    }

    private async Task AddFileToArchiveAsync(AppConfig config, BucketItem file, ZipArchive archive, string entryPath, CancellationToken cancellationToken)
    {
        var zipEntry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        await using var zipStream = zipEntry.Open();
        await _bucketContentService.DownloadFileAsync(config, file, zipStream, cancellationToken);
    }

    private static string GetUniqueZipEntryName(string baseName, ISet<string> usedNames)
    {
        baseName = string.IsNullOrWhiteSpace(baseName) ? "selection" : baseName;
        var candidate = baseName;
        var suffix = 2;
        while (!usedNames.Add(candidate))
        {
            candidate = $"{baseName} ({suffix++})";
        }

        return candidate;
    }
}
