namespace DropAndForget.Models;

public class AppConfig
{
    public StorageMode StorageMode { get; set; } = StorageMode.Cloud;

    public bool IsEncryptionEnabled { get; set; }

    public bool EncryptionBootstrapCompleted { get; set; }

    public string EndpointOrAccountId { get; set; } = string.Empty;

    public string BucketName { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    public string SyncFolderPath { get; set; } = string.Empty;
}
