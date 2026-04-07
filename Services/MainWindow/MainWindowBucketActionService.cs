using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;
using DropAndForget.Services.BucketActions;
using DropAndForget.ViewModels;

namespace DropAndForget.Services.MainWindow;

public sealed class MainWindowBucketActionService(BucketActionWorkflow bucketActionWorkflow)
{
    private readonly BucketActionWorkflow _bucketActionWorkflow = bucketActionWorkflow;

    public Task UploadDroppedFilesAsync(
        AppConfig config,
        IReadOnlyList<string> paths,
        string currentPrefix,
        bool isSyncMode,
        bool isEncryptionEnabled,
        Action<string> setStatus,
        CancellationToken cancellationToken = default)
    {
        return _bucketActionWorkflow.UploadDroppedFilesAsync(
            config,
            paths,
            currentPrefix,
            isSyncMode,
            isEncryptionEnabled,
            setStatus,
            cancellationToken);
    }

    public async Task<string> CreateFolderAsync(
        AppConfig config,
        string folderName,
        IReadOnlyCollection<BucketItem> visibleItems,
        string currentPrefix,
        CancellationToken cancellationToken = default)
    {
        await _bucketActionWorkflow.CreateFolderAsync(config, folderName, visibleItems, currentPrefix, cancellationToken);
        return $"Created folder {BucketUiHelpers.NormalizeItemName(folderName)}.";
    }

    public async Task<BucketRenameResult> RenameAsync(
        AppConfig config,
        BucketListEntry item,
        string newName,
        IReadOnlyCollection<BucketItem> visibleItems,
        CancellationToken cancellationToken = default)
    {
        newName = BucketUiHelpers.NormalizeItemName(newName);

        if (string.Equals(newName, item.DisplayName, StringComparison.Ordinal))
        {
            return new BucketRenameResult(true, false, item, $"{item.KindText} name unchanged.");
        }

        await _bucketActionWorkflow.RenameAsync(config, item.Item, newName, visibleItems, cancellationToken);
        var statusMessage = item.IsFolder
            ? $"Renamed folder {item.DisplayName} to {newName}."
            : $"Renamed file {item.DisplayName} to {newName}.";
        return new BucketRenameResult(true, true, item, statusMessage);
    }

    public async Task<string> DownloadFileAsync(AppConfig config, BucketListEntry item, Stream destination, CancellationToken cancellationToken = default)
    {
        await _bucketActionWorkflow.DownloadFileAsync(config, item.Item, destination, cancellationToken);
        return $"Downloaded {item.DisplayName}.";
    }

    public async Task<string> DownloadFolderAsZipAsync(AppConfig config, BucketListEntry item, Stream destination, CancellationToken cancellationToken = default)
    {
        await _bucketActionWorkflow.DownloadFolderAsZipAsync(config, item.Item, destination, cancellationToken);
        return $"Downloaded {item.DisplayName} as zip.";
    }

    public async Task<string> DownloadAsZipAsync(AppConfig config, IReadOnlyList<BucketListEntry> items, Stream destination, CancellationToken cancellationToken = default)
    {
        await _bucketActionWorkflow.DownloadAsZipAsync(config, items.Select(static item => item.Item).ToList(), destination, cancellationToken);
        return $"Downloaded {items.Count} selected item{(items.Count == 1 ? string.Empty : "s")} as zip.";
    }

    public async Task<FilePreviewResult> LoadPreviewAsync(AppConfig config, BucketListEntry item, CancellationToken cancellationToken = default)
    {
        var preview = await _bucketActionWorkflow.LoadPreviewAsync(config, item.Item, cancellationToken);
        return new FilePreviewResult(preview, $"Loaded preview for {item.DisplayName}.");
    }

    public async Task<string> DeleteAsync(AppConfig config, BucketListEntry item, CancellationToken cancellationToken = default)
    {
        var deletedCount = await _bucketActionWorkflow.DeleteAsync(config, item.Item, cancellationToken);
        return item.IsFolder
            ? deletedCount == 0
                ? $"Folder {item.DisplayName} already empty."
                : $"Deleted folder {item.DisplayName} and {deletedCount} object(s)."
            : $"Deleted file {item.DisplayName}.";
    }

    public async Task<string> DeleteAsync(AppConfig config, IReadOnlyList<BucketListEntry> items, CancellationToken cancellationToken = default)
    {
        var deletedCount = await _bucketActionWorkflow.DeleteAsync(config, items.Select(static item => item.Item).ToList(), cancellationToken);
        return $"Deleted {items.Count} selected item{(items.Count == 1 ? string.Empty : "s")} and {deletedCount} object(s).";
    }

    public async Task<string> MoveAsync(AppConfig config, BucketListEntry item, BucketListEntry targetFolder, CancellationToken cancellationToken = default)
    {
        await _bucketActionWorkflow.MoveAsync(config, item.Item, targetFolder.Item, cancellationToken);
        return item.IsFolder
            ? $"Moved folder {item.DisplayName} to {targetFolder.DisplayName}."
            : $"Moved file {item.DisplayName} to {targetFolder.DisplayName}.";
    }

    public async Task<string> MoveAsync(AppConfig config, BucketListEntry item, string targetFolderPath, string targetLabel, CancellationToken cancellationToken = default)
    {
        await _bucketActionWorkflow.MoveAsync(config, item.Item, targetFolderPath, cancellationToken);
        return item.IsFolder
            ? $"Moved folder {item.DisplayName} to {targetLabel}."
            : $"Moved file {item.DisplayName} to {targetLabel}.";
    }

    public async Task<string> MoveAsync(AppConfig config, IReadOnlyList<BucketListEntry> items, BucketListEntry targetFolder, CancellationToken cancellationToken = default)
    {
        await _bucketActionWorkflow.MoveAsync(config, items.Select(static item => item.Item).ToList(), targetFolder.Item, cancellationToken);
        return $"Moved {items.Count} item{(items.Count == 1 ? string.Empty : "s")} to {targetFolder.DisplayName}.";
    }

    public async Task<string> MoveAsync(AppConfig config, IReadOnlyList<BucketListEntry> items, string targetFolderPath, string targetLabel, CancellationToken cancellationToken = default)
    {
        await _bucketActionWorkflow.MoveAsync(config, items.Select(static item => item.Item).ToList(), targetFolderPath, cancellationToken);
        return $"Moved {items.Count} item{(items.Count == 1 ? string.Empty : "s")} to {targetLabel}.";
    }

    public async Task<CreatePendingFolderResult> CreatePendingFolderAsync(
        AppConfig config,
        BucketListEntry item,
        string folderName,
        IReadOnlyCollection<BucketItem> visibleItems,
        CancellationToken cancellationToken = default)
    {
        folderName = BucketUiHelpers.NormalizeItemName(folderName);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return new CreatePendingFolderResult(false, item.FolderPath, "Folder name required.");
        }

        if (!BucketUiHelpers.IsValidItemName(folderName))
        {
            return new CreatePendingFolderResult(false, item.FolderPath, "Folder name can't contain / or \\.");
        }

        if (BucketUiHelpers.NameExists(visibleItems, folderName, item.Item))
        {
            return new CreatePendingFolderResult(false, item.FolderPath, $"Folder {folderName} already exists.");
        }

        await _bucketActionWorkflow.CreatePendingFolderAsync(config, item.Item, folderName, visibleItems, cancellationToken);
        return new CreatePendingFolderResult(true, item.FolderPath, $"Created folder {folderName}.");
    }
}

public sealed record BucketRenameResult(bool Success, bool RequiresRefresh, BucketListEntry Item, string StatusMessage);

public sealed record FilePreviewResult(FilePreviewData? Preview, string StatusMessage);

public sealed record CreatePendingFolderResult(bool Success, string NextPrefix, string StatusMessage);
