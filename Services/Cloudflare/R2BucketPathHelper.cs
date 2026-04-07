using System;
using System.IO;
using DropAndForget.Models;

namespace DropAndForget.Services.Cloudflare;

internal static class R2BucketPathHelper
{
    internal static string BuildObjectKey(string filePath, string? prefix, string? relativeObjectPath)
    {
        var fileName = string.IsNullOrWhiteSpace(relativeObjectPath)
            ? Path.GetFileName(filePath)
            : relativeObjectPath.Replace('\\', '/').Trim('/');
        var normalizedPrefix = NormalizePrefix(prefix);

        return string.IsNullOrEmpty(normalizedPrefix)
            ? fileName
            : normalizedPrefix + fileName;
    }

    internal static string BuildRenamedKey(BucketItem item, string newDisplayName)
    {
        var trimmedKey = item.IsFolder
            ? item.Key.TrimEnd('/')
            : item.Key;
        var slashIndex = trimmedKey.LastIndexOf('/');
        var parentPrefix = slashIndex >= 0
            ? trimmedKey[..(slashIndex + 1)]
            : string.Empty;
        var normalizedName = item.IsFolder
            ? newDisplayName.Trim().TrimEnd('/') + "/"
            : newDisplayName.Trim();

        return parentPrefix + normalizedName;
    }

    internal static string BuildMovedKey(BucketItem item, string targetFolderPath)
    {
        var normalizedTargetPrefix = NormalizePrefix(targetFolderPath);
        var normalizedName = item.IsFolder
            ? item.DisplayName.Trim().TrimEnd('/') + "/"
            : item.DisplayName.Trim();

        return normalizedTargetPrefix + normalizedName;
    }

    internal static string NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        var normalized = prefix.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(normalized)
            ? string.Empty
            : normalized + "/";
    }

    internal static string GetDisplayName(string key, string? prefix)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        var relativeKey = string.IsNullOrEmpty(normalizedPrefix) || !key.StartsWith(normalizedPrefix, StringComparison.Ordinal)
            ? key
            : key[normalizedPrefix.Length..];

        var trimmed = relativeKey.TrimEnd('/');
        var slashIndex = trimmed.LastIndexOf('/');

        return slashIndex >= 0
            ? trimmed[(slashIndex + 1)..]
            : trimmed;
    }

    internal static string GetParentPath(string key)
    {
        var trimmed = key.TrimEnd('/');
        var slashIndex = trimmed.LastIndexOf('/');

        return slashIndex >= 0
            ? trimmed[..slashIndex]
            : string.Empty;
    }
}
