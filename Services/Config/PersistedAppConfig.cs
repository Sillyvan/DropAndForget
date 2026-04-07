using DropAndForget.Models;

namespace DropAndForget.Services.Config;

public sealed class PersistedAppConfig
{
    public StorageMode StorageMode { get; set; } = StorageMode.Cloud;

    public bool IsEncryptionEnabled { get; set; }

    public bool EncryptionBootstrapCompleted { get; set; }

    public string EndpointOrAccountId { get; set; } = string.Empty;

    public string BucketName { get; set; } = string.Empty;

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    public string EncryptedAccessKeyId { get; set; } = string.Empty;

    public string EncryptedSecretAccessKey { get; set; } = string.Empty;

    public string SyncFolderPath { get; set; } = string.Empty;

    public AppConfig ToAppConfig(LocalSecretProtector secretProtector)
    {
        return new AppConfig
        {
            StorageMode = StorageMode,
            IsEncryptionEnabled = IsEncryptionEnabled,
            EncryptionBootstrapCompleted = EncryptionBootstrapCompleted,
            EndpointOrAccountId = EndpointOrAccountId,
            BucketName = BucketName,
            AccessKeyId = secretProtector.Unprotect(string.IsNullOrWhiteSpace(EncryptedAccessKeyId) ? AccessKeyId ?? string.Empty : EncryptedAccessKeyId),
            SecretAccessKey = secretProtector.Unprotect(string.IsNullOrWhiteSpace(EncryptedSecretAccessKey) ? SecretAccessKey ?? string.Empty : EncryptedSecretAccessKey),
            SyncFolderPath = SyncFolderPath
        };
    }

    public static PersistedAppConfig FromAppConfig(AppConfig config, LocalSecretProtector secretProtector)
    {
        return new PersistedAppConfig
        {
            StorageMode = config.StorageMode,
            IsEncryptionEnabled = config.IsEncryptionEnabled,
            EncryptionBootstrapCompleted = config.EncryptionBootstrapCompleted,
            EndpointOrAccountId = config.EndpointOrAccountId,
            BucketName = config.BucketName,
            EncryptedAccessKeyId = secretProtector.Protect(config.AccessKeyId),
            EncryptedSecretAccessKey = secretProtector.Protect(config.SecretAccessKey),
            SyncFolderPath = config.SyncFolderPath
        };
    }
}
