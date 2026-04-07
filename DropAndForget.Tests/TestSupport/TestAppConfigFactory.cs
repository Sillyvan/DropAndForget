using DropAndForget.Models;

namespace DropAndForget.Tests.TestSupport;

internal static class TestAppConfigFactory
{
    public static AppConfig Create(
        StorageMode storageMode = StorageMode.Cloud,
        bool isEncryptionEnabled = false,
        bool encryptionBootstrapCompleted = false,
        string? syncFolderPath = null)
    {
        return new AppConfig
        {
            StorageMode = storageMode,
            IsEncryptionEnabled = isEncryptionEnabled,
            EncryptionBootstrapCompleted = encryptionBootstrapCompleted,
            EndpointOrAccountId = "account-id",
            BucketName = "bucket-name",
            AccessKeyId = "access-key",
            SecretAccessKey = "secret-key",
            SyncFolderPath = syncFolderPath ?? string.Empty
        };
    }
}
