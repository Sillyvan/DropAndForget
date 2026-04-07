using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;
using DropAndForget.Services.Config;
using DropAndForget.Services.ConnectionWorkflow;
using DropAndForget.Services.Encryption;
using DropAndForget.Services.Sync;

namespace DropAndForget.Services.MainWindow;

public sealed class MainWindowConnectionService(
    JsonSettingsStore settingsStore,
    AppConfigValidator configValidator,
    ConnectionWorkflowService connectionWorkflowService,
    IEncryptedBucketService encryptedBucketService,
    IStorageModeCoordinator storageModeCoordinator)
{
    private readonly JsonSettingsStore _settingsStore = settingsStore;
    private readonly AppConfigValidator _configValidator = configValidator;
    private readonly ConnectionWorkflowService _connectionWorkflowService = connectionWorkflowService;
    private readonly IEncryptedBucketService _encryptedBucketService = encryptedBucketService;
    private readonly IStorageModeCoordinator _storageModeCoordinator = storageModeCoordinator;

    public SavedConfigLoadResult LoadSavedConfig()
    {
        return _settingsStore.Load();
    }

    public async Task<AppConfig> RefreshSavedRemoteStateAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        var remoteState = await _encryptedBucketService.GetRemoteStateAsync(config, cancellationToken);
        if (remoteState == EncryptedBucketRemoteState.Encrypted)
        {
            ApplyEncryptedCloudState(config);
        }
        else
        {
            ApplyPlainCloudState(config);
        }

        await PersistAndApplyAsync(config, cancellationToken);
        return config;
    }

    public async Task<ConnectionPreparationResult> PrepareAndApplyAsync(
        AppConfig config,
        string setupPassphrase,
        string confirmSetupPassphrase,
        bool encryptionBootstrapCompleted,
        bool requireConnectionTest,
        CancellationToken cancellationToken = default)
    {
        var prepared = await _connectionWorkflowService.PrepareAsync(
            config,
            setupPassphrase,
            confirmSetupPassphrase,
            encryptionBootstrapCompleted,
            requireConnectionTest,
            cancellationToken);

        await PersistAndApplyAsync(prepared.Config, cancellationToken);
        return prepared;
    }

    public Task ApplySavedConfigAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        return _storageModeCoordinator.ApplyAsync(config, cancellationToken);
    }

    public async Task<string> UnlockAsync(AppConfig config, string passphrase, CancellationToken cancellationToken = default)
    {
        await _connectionWorkflowService.UnlockAsync(config, passphrase, cancellationToken);
        return "Encrypted bucket unlocked.";
    }

    public void Lock()
    {
        _encryptedBucketService.Lock();
    }

    public async Task<string> ChangePassphraseAsync(
        AppConfig config,
        string currentPassphrase,
        string nextPassphrase,
        string confirmNextPassphrase,
        CancellationToken cancellationToken = default)
    {
        await _connectionWorkflowService.ChangePassphraseAsync(
            config,
            currentPassphrase,
            nextPassphrase,
            confirmNextPassphrase,
            cancellationToken);

        return "Passphrase changed.";
    }

    public async Task<AppConfig> DeleteEncryptedBucketAndDisableAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        await _connectionWorkflowService.DeleteEncryptedBucketAsync(config, cancellationToken);
        _encryptedBucketService.Lock();

        ApplyPlainCloudState(config);

        await PersistAndApplyAsync(config, cancellationToken);
        return config;
    }

    private async Task PersistAndApplyAsync(AppConfig config, CancellationToken cancellationToken)
    {
        _configValidator.ValidatePersistableConfig(config);
        _settingsStore.Save(config);
        await _storageModeCoordinator.ApplyAsync(config, cancellationToken);
    }

    private static void ApplyEncryptedCloudState(AppConfig config)
    {
        config.StorageMode = StorageMode.Cloud;
        config.IsEncryptionEnabled = true;
        config.EncryptionBootstrapCompleted = true;
    }

    private static void ApplyPlainCloudState(AppConfig config)
    {
        config.StorageMode = StorageMode.Cloud;
        config.IsEncryptionEnabled = false;
        config.EncryptionBootstrapCompleted = false;
    }
}

public sealed record ConnectionPreparationResult(
    AppConfig Config,
    bool EncryptionBootstrapCompleted,
    bool IsEncryptedLocked,
    bool ClearSetupPassphrases);
