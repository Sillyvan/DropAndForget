using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DropAndForget.Models;

namespace DropAndForget.Services.Encryption;

internal static class EncryptedBucketIndexView
{
    internal static IReadOnlyList<EncryptedIndexEntry> GetVisibleEntries(EncryptedIndex index, string normalizedPrefix)
    {
        if (string.IsNullOrEmpty(normalizedPrefix))
        {
            return index.Entries
                .Where(entry => !entry.RelativePath.Contains('/'))
                .OrderByDescending(entry => entry.IsFolder)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return index.Entries
            .Where(entry => entry.RelativePath.StartsWith(normalizedPrefix, StringComparison.Ordinal))
            .Where(entry => !entry.RelativePath[normalizedPrefix.Length..].Contains('/'))
            .OrderByDescending(entry => entry.IsFolder)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static BucketItem CreateBucketItem(EncryptedIndexEntry entry)
    {
        return new BucketItem
        {
            Key = entry.IsFolder ? EncryptedBucketPathHelper.NormalizePrefix(entry.RelativePath) : entry.RelativePath,
            DisplayName = entry.DisplayName,
            Detail = entry.IsFolder
                ? "folder"
                : FormatFileDetail(entry.Size, entry.ModifiedAtUtc),
            FolderPath = EncryptedBucketPathHelper.GetParentFolder(entry.RelativePath),
            IsFolder = entry.IsFolder,
            SizeBytes = entry.IsFolder ? null : entry.Size,
            SizeText = entry.IsFolder ? "--" : FormatSize(entry.Size),
            ModifiedAt = entry.ModifiedAtUtc,
            ModifiedText = entry.ModifiedAtUtc == DateTime.MinValue ? string.Empty : entry.ModifiedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        };
    }

    internal static EncryptedIndex CloneIndex(EncryptedIndex index)
    {
        return new EncryptedIndex
        {
            Version = index.Version,
            UpdatedAtUtc = index.UpdatedAtUtc,
            Entries = index.Entries.Select(entry => new EncryptedIndexEntry
            {
                RelativePath = entry.RelativePath,
                DisplayName = entry.DisplayName,
                IsFolder = entry.IsFolder,
                ObjectId = entry.ObjectId,
                WrappedFileKey = entry.WrappedFileKey,
                CipherAlgorithm = entry.CipherAlgorithm,
                Size = entry.Size,
                ContentType = entry.ContentType,
                CreatedAtUtc = entry.CreatedAtUtc,
                ModifiedAtUtc = entry.ModifiedAtUtc
            }).ToList()
        };
    }

    internal static string GuessContentType(string relativePath)
    {
        var extension = System.IO.Path.GetExtension(relativePath).ToLowerInvariant();
        return extension switch
        {
            ".txt" or ".md" or ".json" or ".csv" or ".log" or ".xml" or ".yml" or ".yaml" or ".html" or ".css" or ".js" or ".ts" or ".cs" => "text/plain",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }

    private static string FormatFileDetail(long size, DateTime lastModifiedUtc)
    {
        return $"{FormatSize(size)} - {lastModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
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
