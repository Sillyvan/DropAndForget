using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Amazon.S3.Model;
using DropAndForget.Models;

namespace DropAndForget.Services.Cloudflare;

internal static class R2BucketMapper
{
    internal static IReadOnlyList<BucketItem> BuildBucketItems(ListObjectsV2Response response, string? prefix)
    {
        var items = new List<BucketItem>();
        var folderKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var folder in response.CommonPrefixes ?? [])
        {
            var normalizedFolder = R2BucketPathHelper.NormalizePrefix(folder);
            if (string.IsNullOrEmpty(normalizedFolder))
            {
                continue;
            }

            folderKeys.Add(normalizedFolder);

            items.Add(new BucketItem
            {
                Key = normalizedFolder,
                DisplayName = R2BucketPathHelper.GetDisplayName(normalizedFolder, prefix),
                Detail = "folder",
                FolderPath = R2BucketPathHelper.GetParentPath(normalizedFolder),
                IsFolder = true,
                SizeText = "--",
                ModifiedText = "--"
            });
        }

        foreach (var obj in response.S3Objects ?? [])
        {
            if (string.Equals(obj.Key, prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (obj.Key.EndsWith("/", StringComparison.Ordinal) && folderKeys.Contains(R2BucketPathHelper.NormalizePrefix(obj.Key)))
            {
                continue;
            }

            items.Add(CreateFileBucketItem(obj, prefix));
        }

        return items
            .OrderByDescending(item => item.IsFolder)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static BucketItem CreateFileBucketItem(S3Object obj, string? prefix = null)
    {
        var size = obj.Size ?? 0;
        var lastModified = obj.LastModified ?? DateTime.MinValue;

        return new BucketItem
        {
            Key = obj.Key,
            DisplayName = R2BucketPathHelper.GetDisplayName(obj.Key, prefix),
            Detail = FormatFileDetail(size, lastModified),
            FolderPath = R2BucketPathHelper.GetParentPath(obj.Key),
            IsFolder = false,
            SizeBytes = size,
            SizeText = FormatSize(size),
            ModifiedAt = lastModified,
            ModifiedText = lastModified == DateTime.MinValue ? string.Empty : lastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        };
    }

    internal static R2ObjectInfo CreateObjectInfo(S3Object obj)
    {
        return new R2ObjectInfo
        {
            Key = obj.Key,
            IsFolder = obj.Key.EndsWith("/", StringComparison.Ordinal),
            Size = obj.Size ?? 0,
            LastModifiedUtc = (obj.LastModified ?? DateTime.MinValue).ToUniversalTime(),
            ETag = obj.ETag ?? string.Empty
        };
    }

    private static string FormatFileDetail(long size, DateTime lastModified)
    {
        return $"{FormatSize(size)} - {lastModified.ToLocalTime():yyyy-MM-dd HH:mm}";
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

        return value.ToString("0.#", CultureInfo.InvariantCulture) + " " + suffixes[suffixIndex];
    }
}
