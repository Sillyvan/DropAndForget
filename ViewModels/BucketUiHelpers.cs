using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DropAndForget.Models;
using DropAndForget.Services.Sync;

namespace DropAndForget.ViewModels;

internal static class BucketUiHelpers
{
    private static readonly HashSet<string> ImagePreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
    };

    private static readonly HashSet<string> TextPreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".csv", ".log", ".xml", ".yml", ".yaml", ".html", ".htm", ".css", ".js", ".ts", ".cs"
    };

    internal static string FormatSize(long size)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = size;
        var suffixIndex = 0;

        while (value >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        return value.ToString("0.#", CultureInfo.InvariantCulture) + " " + suffixes[suffixIndex];
    }

    internal static FilePreviewKind? GetPreviewKind(BucketItem item)
    {
        var extension = Path.GetExtension(item.Key);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        if (ImagePreviewExtensions.Contains(extension))
        {
            return FilePreviewKind.Image;
        }

        if (TextPreviewExtensions.Contains(extension))
        {
            return FilePreviewKind.Text;
        }

        return string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase)
            ? FilePreviewKind.Pdf
            : null;
    }

    internal static string ReadText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    internal static IReadOnlyList<BreadcrumbItem> BuildBreadcrumbs(string currentPrefix)
    {
        var items = new List<BreadcrumbItem>
        {
            new()
            {
                Label = "/",
                Prefix = string.Empty,
                IsCurrent = string.IsNullOrEmpty(currentPrefix)
            }
        };

        if (string.IsNullOrEmpty(currentPrefix))
        {
            return items;
        }

        var parts = currentPrefix.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var prefix = string.Empty;

        foreach (var part in parts)
        {
            prefix += part + "/";
            items.Add(new BreadcrumbItem
            {
                Label = part,
                Prefix = prefix,
                IsCurrent = string.Equals(prefix, currentPrefix, StringComparison.Ordinal)
            });
        }

        return items;
    }

    internal static List<UploadRequest> ExpandDroppedPaths(IEnumerable<string> paths)
    {
        var uploads = new List<UploadRequest>();

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                uploads.Add(new UploadRequest(path, Path.GetFileName(path)));
                continue;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            var directoryName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                continue;
            }

            var directoryParent = Directory.GetParent(path)?.FullName;
            if (string.IsNullOrWhiteSpace(directoryParent))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(directoryParent, file).Replace('\\', '/');
                uploads.Add(new UploadRequest(file, relativePath));
            }
        }

        return uploads;
    }

    internal static string BuildNewFolderName(IEnumerable<BucketItem> items)
    {
        const string baseName = "New folder";
        var existingNames = new HashSet<string>(items.Select(item => item.DisplayName), StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{baseName} {index}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    internal static string NormalizeItemName(string name)
    {
        return name.Trim();
    }

    internal static bool IsValidItemName(string name)
    {
        return !name.Contains('/') && !name.Contains('\\');
    }

    internal static bool NameExists(IEnumerable<BucketItem> items, string name, BucketItem? excludedItem = null)
    {
        return items.Any(item =>
            !ReferenceEquals(item, excludedItem)
            && string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase));
    }

    internal static void ApplySyncVisualState(BucketListEntry item, bool isSyncMode, bool isEncryptionEnabled, IStorageModeCoordinator storageModeCoordinator)
    {
        if (!isSyncMode)
        {
            item.SyncStatusText = isEncryptionEnabled ? "Encrypted" : "Cloud";
            item.IsSyncSynced = true;
            item.IsSyncPending = false;
            item.IsSyncing = false;
            return;
        }

        var visualState = storageModeCoordinator.GetVisualState(item.Key, item.IsFolder);
        item.SyncStatusText = visualState switch
        {
            SyncVisualState.Syncing => "Syncing",
            SyncVisualState.Pending => "Not synced",
            _ => "Synced"
        };
        item.IsSyncSynced = visualState == SyncVisualState.Synced;
        item.IsSyncPending = visualState == SyncVisualState.Pending;
        item.IsSyncing = visualState == SyncVisualState.Syncing;
    }
}

internal sealed record UploadRequest(string FilePath, string RelativeObjectPath)
{
    internal string DisplayName => RelativeObjectPath;
}
