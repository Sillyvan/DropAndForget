using System.IO;

namespace DropAndForget.Services.Encryption;

internal static class EncryptedBucketPathHelper
{
    internal static string BuildRelativePath(string filePath, string? prefix, string? relativeObjectPath)
    {
        var name = string.IsNullOrWhiteSpace(relativeObjectPath)
            ? Path.GetFileName(filePath)
            : relativeObjectPath.Replace('\\', '/').Trim('/');
        return CombineRelative(prefix, name);
    }

    internal static string CombineRelative(string? prefix, string name)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        var normalizedName = name.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(normalizedPrefix)
            ? normalizedName
            : normalizedPrefix + normalizedName;
    }

    internal static string NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        var normalized = prefix.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(normalized) ? string.Empty : normalized + "/";
    }

    internal static string GetName(string relativePath)
    {
        var trimmed = relativePath.TrimEnd('/');
        var slashIndex = trimmed.LastIndexOf('/');
        return slashIndex >= 0 ? trimmed[(slashIndex + 1)..] : trimmed;
    }

    internal static string GetParentPrefix(string relativePath)
    {
        var slashIndex = relativePath.LastIndexOf('/');
        return slashIndex < 0 ? string.Empty : relativePath[..(slashIndex + 1)];
    }

    internal static string GetParentFolder(string relativePath)
    {
        var parent = GetParentPrefix(relativePath);
        return string.IsNullOrEmpty(parent) ? string.Empty : parent;
    }
}
