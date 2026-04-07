using System;
using System.Threading.Tasks;
using System.Windows.Input;
using DropAndForget.Models;
using DropAndForget.Services.Cloudflare;
using DropAndForget.Services.Config;
using DropAndForget.Services.Diagnostics;
using DropAndForget.Services.MainWindow;

namespace DropAndForget.ViewModels;

public sealed class ConnectionSetupViewModel : ViewModelBase
{
    private readonly AppConfigValidator _configValidator;
    private readonly MainWindowConnectionService _connectionService;
    private readonly AsyncRelayCommand _deleteEncryptedBucketCommand;
    private readonly AsyncRelayCommand _saveDraftCommand;
    private readonly AsyncRelayCommand _testConnectionCommand;
    private readonly AsyncRelayCommand _unlockDetectedEncryptedBucketCommand;
    private string _accessKeyId = string.Empty;
    private string _bucketName = string.Empty;
    private string _confirmSetupPassphrase = string.Empty;
    private string _detectedBucketPassphrase = string.Empty;
    private string _endpointOrAccountId = string.Empty;
    private bool _encryptionBootstrapCompleted;
    private bool _isDetectedEncryptedBucketPendingAction;
    private bool _isBusy;
    private bool _isEncryptionEnabled;
    private string _secretAccessKey = string.Empty;
    private string _setupPassphrase = string.Empty;
    private string _statusMessage = string.Empty;
    private StorageMode _storageMode;
    private string _syncFolderPath = string.Empty;
    private string _syncStatus = "Sync off.";

    public ConnectionSetupViewModel(AppConfigValidator configValidator, MainWindowConnectionService connectionService)
    {
        _configValidator = configValidator;
        _connectionService = connectionService;
        SuggestedBucketName = $"dropandforget-{Guid.NewGuid():N}"[..22];

        OpenCreateBucketCommand = new RelayCommand(() => MainWindowUiSupport.OpenUrl("https://dash.cloudflare.com/?to=/:account/r2/new"));
        OpenApiTokensCommand = new RelayCommand(() => MainWindowUiSupport.OpenUrl("https://dash.cloudflare.com/?to=/:account/r2/api-tokens/create?type=user"));
        UseSuggestedBucketNameCommand = new RelayCommand(() => BucketName = SuggestedBucketName);

        _saveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, CanUseConnectionActions, HandleUnexpectedCommandException);
        SaveDraftCommand = _saveDraftCommand;

        _testConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanUseConnectionActions, HandleUnexpectedCommandException);
        TestConnectionCommand = _testConnectionCommand;

        _unlockDetectedEncryptedBucketCommand = new AsyncRelayCommand(UnlockDetectedEncryptedBucketAsync, CanUnlockDetectedEncryptedBucket, HandleUnexpectedCommandException);
        UnlockDetectedEncryptedBucketCommand = _unlockDetectedEncryptedBucketCommand;

        _deleteEncryptedBucketCommand = new AsyncRelayCommand(DeleteEncryptedBucketAsync, CanDeleteEncryptedBucket, HandleUnexpectedCommandException);
        DeleteEncryptedBucketCommand = _deleteEncryptedBucketCommand;

        StatusMessage = "Configure bucket connection.";
    }

    public event Func<Task>? ConnectionTestSucceeded;

    public string SuggestedBucketName { get; }

    public string EndpointOrAccountId
    {
        get => _endpointOrAccountId;
        set
        {
            if (SetProperty(ref _endpointOrAccountId, value))
            {
                RaiseCommandStates();
                RaisePropertyChanged(nameof(EndpointSummary));
            }
        }
    }

    public string BucketName
    {
        get => _bucketName;
        set
        {
            if (SetProperty(ref _bucketName, value))
            {
                RaiseCommandStates();
                RaisePropertyChanged(nameof(BucketSummary));
            }
        }
    }

    public string AccessKeyId
    {
        get => _accessKeyId;
        set
        {
            if (SetProperty(ref _accessKeyId, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string SecretAccessKey
    {
        get => _secretAccessKey;
        set
        {
            if (SetProperty(ref _secretAccessKey, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public StorageMode StorageMode
    {
        get => _storageMode;
        set
        {
            if (value == StorageMode.Sync && IsEncryptionEnabled)
            {
                StatusMessage = "Encryption mode can't use sync.";
                value = StorageMode.Cloud;
            }

            if (SetProperty(ref _storageMode, value))
            {
                RaisePropertyChanges(
                    nameof(IsSyncMode),
                    nameof(IsCloudMode),
                    nameof(StorageModeLabel),
                    nameof(StorageModeDescription),
                    nameof(SyncFolderSummary),
                    nameof(IsSyncConfigured));
                RaiseCommandStates();
            }
        }
    }

    public bool IsCloudMode => StorageMode == StorageMode.Cloud;

    public bool IsSyncMode
    {
        get => StorageMode == StorageMode.Sync;
        set
        {
            if (value && IsEncryptionEnabled)
            {
                StatusMessage = "Encryption mode can't use sync.";
                StorageMode = StorageMode.Cloud;
                return;
            }

            StorageMode = value ? StorageMode.Sync : StorageMode.Cloud;
        }
    }

    public bool IsEncryptionEnabled
    {
        get => _isEncryptionEnabled;
        set
        {
            if (!SetProperty(ref _isEncryptionEnabled, value))
            {
                return;
            }

            if (value)
            {
                StorageMode = StorageMode.Cloud;
            }

            RaisePropertyChanges(
                nameof(IsEncryptedModeActive),
                nameof(ShowEncryptedBucketDetectedActions),
                nameof(ShowEncryptionSetupFields),
                nameof(ShowEncryptionAlreadyInitializedText),
                nameof(StorageModeLabel),
                nameof(StorageModeDescription),
                nameof(EncryptionSetupHint),
                nameof(IsSyncToggleEnabled));
            RaiseCommandStates();
        }
    }

    public bool EncryptionBootstrapCompleted
    {
        get => _encryptionBootstrapCompleted;
        private set
        {
            if (SetProperty(ref _encryptionBootstrapCompleted, value))
            {
                RaisePropertyChanges(
                    nameof(NeedsEncryptionSetup),
                    nameof(ShowEncryptedBucketDetectedActions),
                    nameof(ShowEncryptionAlreadyInitializedText),
                    nameof(EncryptionSetupHint));
                RaiseCommandStates();
            }
        }
    }

    public bool IsEncryptedModeActive => IsEncryptionEnabled;

    public bool NeedsEncryptionSetup => IsEncryptionEnabled && !EncryptionBootstrapCompleted;

    public bool IsSyncToggleEnabled => !IsEncryptionEnabled;

    public string StorageModeLabel => IsEncryptionEnabled
        ? "Encrypted cloud"
        : IsSyncMode ? "Sync" : "Cloud";

    public string StorageModeDescription => IsEncryptionEnabled
        ? "Encrypted cloud hides filenames and content from R2. Needs a passphrase every launch. Sync stays off."
        : "Cloud keeps direct R2 behavior. Sync keeps a local folder, watches it, and syncs in the background while the app stays open.";

    public string EncryptionTradeoffText => "What it does: encrypts every file before upload, hides names and folder paths, asks for passphrase every launch, lets multiple PCs open same bucket with same passphrase.";

    public string EncryptionLossesText => "What you lose: no sync mode, no bucket browsing before unlock, forgotten passphrase means permanent loss, direct R2 listing becomes random ids only.";

    public string EncryptionPerformanceText => "Performance: unlock runs Argon2id so startup is slower, uploads/downloads do local encrypt/decrypt work, big previews can use more RAM.";

    public string ShowSetupDescription => "Manage your R2 connection.";

    public bool ShowEncryptedBucketDetectedActions => IsEncryptionEnabled && IsDetectedEncryptedBucketPendingAction;

    public bool ShowEncryptionSetupFields => IsEncryptionEnabled && !IsDetectedEncryptedBucketPendingAction;

    public bool ShowEncryptionAlreadyInitializedText => IsEncryptionEnabled && EncryptionBootstrapCompleted && !IsDetectedEncryptedBucketPendingAction;

    public string EncryptionSetupHint => IsDetectedEncryptedBucketPendingAction
        ? "Encrypted bucket detected. Unlock with the existing passphrase, or delete the whole bucket to disable encryption."
        : EncryptionBootstrapCompleted
        ? "Encryption already initialized for this bucket. These setup fields are ignored now. Use unlock in the bucket view, then Change passphrase after unlock."
        : "Use a passphrase you can keep forever. If you lose it, files stay unreadable.";

    public string DetectedBucketWarningText => "Delete encrypted bucket removes every object in the bucket. This can't be undone.";

    public string SetupPassphrase
    {
        get => _setupPassphrase;
        set
        {
            if (SetProperty(ref _setupPassphrase, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string ConfirmSetupPassphrase
    {
        get => _confirmSetupPassphrase;
        set
        {
            if (SetProperty(ref _confirmSetupPassphrase, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsDetectedEncryptedBucketPendingAction
    {
        get => _isDetectedEncryptedBucketPendingAction;
        private set
        {
            if (SetProperty(ref _isDetectedEncryptedBucketPendingAction, value))
            {
                RaisePropertyChanges(
                    nameof(ShowEncryptedBucketDetectedActions),
                    nameof(ShowEncryptionSetupFields),
                    nameof(ShowEncryptionAlreadyInitializedText),
                    nameof(EncryptionSetupHint));
                RaiseCommandStates();
            }
        }
    }

    public string DetectedBucketPassphrase
    {
        get => _detectedBucketPassphrase;
        set
        {
            if (SetProperty(ref _detectedBucketPassphrase, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string SyncFolderPath
    {
        get => _syncFolderPath;
        private set
        {
            if (SetProperty(ref _syncFolderPath, value))
            {
                RaisePropertyChanges(nameof(SyncFolderSummary), nameof(IsSyncConfigured));
                RaiseCommandStates();
            }
        }
    }

    public string SyncFolderSummary => string.IsNullOrWhiteSpace(SyncFolderPath)
        ? "No sync folder yet"
        : $"Sync folder: {SyncFolderPath}";

    public string SyncStatus
    {
        get => _syncStatus;
        private set => SetProperty(ref _syncStatus, value);
    }

    public string BucketSummary => string.IsNullOrWhiteSpace(BucketName)
        ? "No bucket yet"
        : $"Bucket: {BucketName}";

    public string EndpointSummary => string.IsNullOrWhiteSpace(EndpointOrAccountId)
        ? string.Empty
        : $"Endpoint: {R2ConnectionValidator.NormalizeEndpoint(EndpointOrAccountId)}";

    public bool IsSyncConfigured => IsSyncMode && !string.IsNullOrWhiteSpace(SyncFolderPath);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public ICommand OpenCreateBucketCommand { get; }

    public ICommand OpenApiTokensCommand { get; }

    public ICommand UseSuggestedBucketNameCommand { get; }

    public ICommand SaveDraftCommand { get; }

    public ICommand TestConnectionCommand { get; }

    public ICommand UnlockDetectedEncryptedBucketCommand { get; }

    public ICommand DeleteEncryptedBucketCommand { get; }

    public bool HasConfiguredConnection => _configValidator.HasConnectionSettings(CreateConfig());

    public AppConfig CreateConfig()
    {
        return new AppConfig
        {
            StorageMode = StorageMode,
            IsEncryptionEnabled = IsEncryptionEnabled,
            EncryptionBootstrapCompleted = EncryptionBootstrapCompleted,
            EndpointOrAccountId = EndpointOrAccountId.Trim(),
            BucketName = BucketName.Trim(),
            AccessKeyId = AccessKeyId.Trim(),
            SecretAccessKey = SecretAccessKey.Trim(),
            SyncFolderPath = SyncFolderPath.Trim()
        };
    }

    public void LoadSavedConfig()
    {
        var result = _connectionService.LoadSavedConfig();
        ApplyConfig(result.Config);
        if (!string.IsNullOrWhiteSpace(result.WarningMessage))
        {
            StatusMessage = result.WarningMessage;
        }
    }

    public void ApplyConfig(AppConfig config)
    {
        StorageMode = config.StorageMode;
        IsEncryptionEnabled = config.IsEncryptionEnabled;
        EncryptionBootstrapCompleted = config.EncryptionBootstrapCompleted;
        EndpointOrAccountId = config.EndpointOrAccountId;
        BucketName = config.BucketName;
        AccessKeyId = config.AccessKeyId;
        SecretAccessKey = config.SecretAccessKey;
        SyncFolderPath = config.SyncFolderPath;
    }

    public void SetSyncFolderPath(string path)
    {
        SyncFolderPath = path.Trim();
    }

    public void SetSyncStatus(string message)
    {
        SyncStatus = message;
    }

    public void SetStatus(string message)
    {
        StatusMessage = message;
    }

    private bool CanUseConnectionActions()
    {
        return HasConfiguredConnection && !IsBusy && !IsDetectedEncryptedBucketPendingAction;
    }

    private bool CanUnlockDetectedEncryptedBucket()
    {
        return HasConfiguredConnection
            && IsDetectedEncryptedBucketPendingAction
            && !IsBusy
            && !string.IsNullOrWhiteSpace(DetectedBucketPassphrase);
    }

    private bool CanDeleteEncryptedBucket()
    {
        return HasConfiguredConnection && IsDetectedEncryptedBucketPendingAction && !IsBusy;
    }

    private async Task SaveDraftAsync()
    {
        try
        {
            IsBusy = true;
            var prepared = await _connectionService.PrepareAndApplyAsync(
                CreateConfig(),
                SetupPassphrase,
                ConfirmSetupPassphrase,
                EncryptionBootstrapCompleted,
                requireConnectionTest: false);
            ApplyPreparedState(prepared);
            StatusMessage = prepared.IsEncryptedLocked
                ? "Encrypted bucket detected. Unlock now or delete it."
                : "Saved.";
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TestConnectionAsync()
    {
        if (!HasConfiguredConnection)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Testing connection...";

        try
        {
            var prepared = await _connectionService.PrepareAndApplyAsync(
                CreateConfig(),
                SetupPassphrase,
                ConfirmSetupPassphrase,
                EncryptionBootstrapCompleted,
                requireConnectionTest: true);
            ApplyPreparedState(prepared);
            if (prepared.IsEncryptedLocked)
            {
                StatusMessage = "Encrypted bucket detected. Unlock now or delete it.";
                return;
            }

            if (ConnectionTestSucceeded is not null)
            {
                await ConnectionTestSucceeded.Invoke();
            }
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = ex.Message;
            TestConnectionFailed?.Invoke(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public event Action<string>? TestConnectionFailed;

    private void ApplyPreparedState(ConnectionPreparationResult prepared)
    {
        ApplyConfig(prepared.Config);
        IsDetectedEncryptedBucketPendingAction = prepared.IsEncryptedLocked;
        EncryptionBootstrapCompleted = prepared.EncryptionBootstrapCompleted;
        if (prepared.ClearSetupPassphrases)
        {
            SetupPassphrase = string.Empty;
            ConfirmSetupPassphrase = string.Empty;
        }

        if (!prepared.IsEncryptedLocked)
        {
            DetectedBucketPassphrase = string.Empty;
        }
    }

    private void RaiseCommandStates()
    {
        _saveDraftCommand.RaiseCanExecuteChanged();
        _testConnectionCommand.RaiseCanExecuteChanged();
        _unlockDetectedEncryptedBucketCommand.RaiseCanExecuteChanged();
        _deleteEncryptedBucketCommand.RaiseCanExecuteChanged();
    }

    private async Task UnlockDetectedEncryptedBucketAsync()
    {
        IsBusy = true;
        StatusMessage = "Unlocking encrypted bucket...";

        try
        {
            StatusMessage = await _connectionService.UnlockAsync(CreateConfig(), DetectedBucketPassphrase);
            DetectedBucketPassphrase = string.Empty;
            IsDetectedEncryptedBucketPendingAction = false;
            if (ConnectionTestSucceeded is not null)
            {
                await ConnectionTestSucceeded.Invoke();
            }
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteEncryptedBucketAsync()
    {
        IsBusy = true;
        StatusMessage = "Deleting encrypted bucket...";

        try
        {
            var updatedConfig = await _connectionService.DeleteEncryptedBucketAndDisableAsync(CreateConfig());
            ApplyConfig(updatedConfig);
            IsDetectedEncryptedBucketPendingAction = false;
            SetupPassphrase = string.Empty;
            ConfirmSetupPassphrase = string.Empty;
            DetectedBucketPassphrase = string.Empty;
            StatusMessage = "Encrypted bucket deleted. Bucket is empty now.";
            if (ConnectionTestSucceeded is not null)
            {
                await ConnectionTestSucceeded.Invoke();
            }
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void HandleUnexpectedCommandException(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            StatusMessage = "Canceled.";
            return;
        }

        DebugLog.Write($"Unexpected setup command error: {ex}");
        StatusMessage = "Unexpected error.";
    }
}
