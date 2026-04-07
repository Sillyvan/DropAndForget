using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;

namespace DropAndForget.Services.Sync;

public class LocalSyncBrowser(ISyncModeService syncModeService) : ILocalSyncBrowser
{
    private readonly ISyncModeService _syncModeService = syncModeService ?? throw new ArgumentNullException(nameof(syncModeService));

    public IReadOnlyList<BucketItem> ListItems(AppConfig config, string prefix)
    {
        var folderPath = GetFolderPath(config, prefix);
        if (!Directory.Exists(folderPath))
        {
            return [];
        }

        return Directory.EnumerateFileSystemEntries(folderPath)
            .Select(path => CreateBucketItem(config, path))
            .OrderByDescending(item => item.IsFolder)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<BucketItem> SearchItems(AppConfig config, string term)
    {
        var rootPath = GetRootPath(config);
        if (!Directory.Exists(rootPath))
        {
            return [];
        }

        return Directory.EnumerateFileSystemEntries(rootPath, "*", SearchOption.AllDirectories)
            .Select(path => CreateBucketItem(config, path))
            .Where(item => item.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.FolderPath.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.FolderPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void CreateFolder(AppConfig config, string currentPrefix, string folderName)
    {
        Directory.CreateDirectory(GetItemPath(config, CombineRelativePath(currentPrefix, folderName)));
    }

    public void RenameItem(AppConfig config, BucketItem item, string newName)
    {
        var sourcePath = GetItemPath(config, item.Key);
        var targetRelativePath = CombineRelativePath(GetFolderPath(item.Key), newName);
        var targetPath = GetItemPath(config, targetRelativePath);

        if (item.IsFolder)
        {
            Directory.Move(sourcePath, targetPath);
            return;
        }

        File.Move(sourcePath, targetPath);
    }

    public void MoveItem(AppConfig config, BucketItem item, string targetFolderPath)
    {
        var sourcePath = GetItemPath(config, item.Key);
        var targetRelativePath = CombineRelativePath(targetFolderPath, item.DisplayName);
        var targetPath = GetItemPath(config, targetRelativePath);
        var targetParentPath = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetParentPath))
        {
            Directory.CreateDirectory(targetParentPath);
        }

        if (item.IsFolder)
        {
            Directory.Move(sourcePath, targetPath);
            return;
        }

        File.Move(sourcePath, targetPath);
    }

    public int DeleteItem(AppConfig config, BucketItem item)
    {
        var path = GetItemPath(config, item.Key);
        if (item.IsFolder)
        {
            var count = Directory.Exists(path)
                ? Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).Count() + 1
                : 0;
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return count;
        }

        if (!File.Exists(path))
        {
            return 0;
        }

        File.Delete(path);
        return 1;
    }

    public async Task CopyFileToAsync(AppConfig config, string relativePath, Stream destination, CancellationToken cancellationToken = default)
    {
        await using var source = File.OpenRead(GetItemPath(config, relativePath));
        await source.CopyToAsync(destination, cancellationToken);
    }

    public Task<byte[]> ReadAllBytesAsync(AppConfig config, string relativePath, CancellationToken cancellationToken = default)
    {
        return File.ReadAllBytesAsync(GetItemPath(config, relativePath), cancellationToken);
    }

    public string GetItemPath(AppConfig config, string relativePath)
    {
        var rootPath = GetRootPath(config);
        if (string.IsNullOrEmpty(relativePath))
        {
            return rootPath;
        }

        return relativePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Aggregate(rootPath, Path.Combine);
    }

    private BucketItem CreateBucketItem(AppConfig config, string path)
    {
        var relativePath = GetRelativePath(config, path);
        if (Directory.Exists(path))
        {
            return new BucketItem
            {
                Key = relativePath,
                DisplayName = Path.GetFileName(path),
                Detail = "folder",
                FolderPath = GetFolderPath(relativePath),
                IsFolder = true,
                SizeText = "--",
                ModifiedText = Directory.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm")
            };
        }

        var fileInfo = new FileInfo(path);
        return new BucketItem
        {
            Key = relativePath,
            DisplayName = Path.GetFileName(path),
            Detail = $"{FormatSize(fileInfo.Length)} - {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm}",
            FolderPath = GetFolderPath(relativePath),
            IsFolder = false,
            SizeBytes = fileInfo.Length,
            SizeText = FormatSize(fileInfo.Length),
            ModifiedAt = fileInfo.LastWriteTime,
            ModifiedText = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
        };
    }

    private string GetRootPath(AppConfig config)
    {
        return _syncModeService.GetSyncFolderPath(config);
    }

    private string GetFolderPath(AppConfig config, string prefix)
    {
        return string.IsNullOrEmpty(prefix)
            ? GetRootPath(config)
            : GetItemPath(config, prefix);
    }

    private string GetRelativePath(AppConfig config, string path)
    {
        var rootPath = GetRootPath(config);
        var relativePath = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
        return relativePath == "." ? string.Empty : relativePath.Trim('/');
    }

    private static string CombineRelativePath(string prefix, string name)
    {
        var normalizedPrefix = prefix.Replace('\\', '/').Trim('/');
        var normalizedName = name.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(normalizedPrefix) ? normalizedName : normalizedPrefix + "/" + normalizedName;
    }

    private static string GetFolderPath(string relativePath)
    {
        var slashIndex = relativePath.LastIndexOf('/');
        return slashIndex < 0 ? string.Empty : relativePath[..slashIndex];
    }

    private static string FormatSize(long size)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = size;
        var suffixIndex = 0;

        while (value >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        return value.ToString("0.#") + " " + suffixes[suffixIndex];
    }
}
