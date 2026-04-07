using System;
using System.IO;
using DropAndForget.Models;

namespace DropAndForget.Services.Config;

public class AppConfigValidator
{
    public bool HasConnectionSettings(AppConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.EndpointOrAccountId)
            && !string.IsNullOrWhiteSpace(config.BucketName)
            && !string.IsNullOrWhiteSpace(config.AccessKeyId)
            && !string.IsNullOrWhiteSpace(config.SecretAccessKey);
    }

    public void ValidateConnectionConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        EnsureRequired(config.EndpointOrAccountId, "Default endpoint required.");
        EnsureRequired(config.BucketName, "Bucket name required.");
        EnsureRequired(config.AccessKeyId, "Access key id required.");
        EnsureRequired(config.SecretAccessKey, "Secret access key required.");
    }

    public void ValidatePersistableConfig(AppConfig config)
    {
        ValidateConnectionConfig(config);

        if (config.IsEncryptionEnabled && config.StorageMode == StorageMode.Sync)
        {
            throw new InvalidOperationException("Encryption mode can't use sync.");
        }

        if (config.StorageMode != StorageMode.Sync)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(config.SyncFolderPath))
        {
            throw new InvalidOperationException("Pick a sync folder first.");
        }

        var fullPath = ExpandHome(config.SyncFolderPath.Trim());
        var invalidChars = Path.GetInvalidPathChars();
        if (fullPath.IndexOfAny(invalidChars) >= 0)
        {
            throw new InvalidOperationException("Sync folder path is invalid.");
        }
    }

    private static string ExpandHome(string path)
    {
        if (!path.StartsWith("~/", StringComparison.Ordinal))
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, path[2..]);
    }

    private static void EnsureRequired(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }
    }
}
