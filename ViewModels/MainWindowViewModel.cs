using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using DropAndForget.Services.Diagnostics;

namespace DropAndForget.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly AsyncRelayCommand _showBucketCommand;
    private bool _isBucketViewVisible;
    private bool _isDropTargetActive;
    private bool _isSetupViewVisible = true;
    private string _statusMessage = string.Empty;

    public MainWindowViewModel(ConnectionSetupViewModel setup, BucketBrowserViewModel bucket)
    {
        Setup = setup;
        Bucket = bucket;
        _statusMessage = "Paste Default endpoint, Access Key ID, and Secret Access Key. Skip Token value.";

        ShowSetupCommand = new RelayCommand(() => ShowSetup("Edit connection."));
        _showBucketCommand = new AsyncRelayCommand(ShowBucketFromCommandAsync, CanShowBucket, HandleUnexpectedCommandException);
        ShowBucketCommand = _showBucketCommand;

        Setup.ConnectionTestSucceeded += OnConnectionTestSucceededAsync;
        Setup.TestConnectionFailed += ShowSetup;
        Setup.PropertyChanged += OnChildPropertyChanged;

        Bucket.SetupRequested += ShowSetup;
        Bucket.PropertyChanged += OnChildPropertyChanged;

        Setup.LoadSavedConfig();
        Bucket.InitializeFromSetup();
        if (Setup.HasConfiguredConnection)
        {
            ShowBucket(Setup.IsEncryptionEnabled && Setup.EncryptionBootstrapCompleted
                ? "Encrypted bucket locked. Enter passphrase."
                : "Loading saved bucket...");
        }

        MainWindowUiSupport.ObserveBackgroundTask(Bucket.TryOpenSavedBucketAsync(), "open saved bucket");
    }

    public ConnectionSetupViewModel Setup { get; }

    public BucketBrowserViewModel Bucket { get; }

    public UI.DialogManager DialogManager => Bucket.DialogManager;

    public bool IsSetupViewVisible
    {
        get => _isSetupViewVisible;
        private set => SetProperty(ref _isSetupViewVisible, value);
    }

    public bool IsBucketViewVisible
    {
        get => _isBucketViewVisible;
        private set => SetProperty(ref _isBucketViewVisible, value);
    }

    public bool IsDropTargetActive
    {
        get => _isDropTargetActive;
        set
        {
            SetProperty(ref _isDropTargetActive, value);
        }
    }

    public bool IsBusy => Setup.IsBusy || Bucket.IsBusy;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand ShowSetupCommand { get; }

    public ICommand ShowBucketCommand { get; }

    public async Task StopSyncAsync()
    {
        await Bucket.StopSyncAsync();
    }

    private bool CanShowBucket()
    {
        return Setup.HasConfiguredConnection;
    }

    private async Task ShowBucketFromCommandAsync()
    {
        ShowBucket("Back to bucket.");
        await Bucket.EnsureBucketShownAsync();
    }

    private async Task OnConnectionTestSucceededAsync()
    {
        DebugLog.Write("MainWindowViewModel connection test succeeded; switching to bucket view");
        ShowBucket("Connected. Loading bucket...");
        await Bucket.HandleConnectionReadyAsync();
        SyncStatusFromVisibleChild();
        DebugLog.Write($"MainWindowViewModel after connection ready setupVisible={IsSetupViewVisible} bucketVisible={IsBucketViewVisible}");
    }

    private void ShowSetup(string message)
    {
        DebugLog.Write("ShowSetup");
        SetVisibleView(showSetup: true, message);
        IsDropTargetActive = false;
    }

    private void ShowBucket(string message)
    {
        DebugLog.Write("ShowBucket");
        SetVisibleView(showSetup: false, message);
    }

    private void SetVisibleView(bool showSetup, string message)
    {
        IsSetupViewVisible = showSetup;
        IsBucketViewVisible = !showSetup;
        StatusMessage = message;
        _showBucketCommand.RaiseCanExecuteChanged();
        RaisePropertyChanged(nameof(IsBusy));
    }

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionSetupViewModel.StatusMessage)
            or nameof(BucketBrowserViewModel.StatusMessage))
        {
            SyncStatusFromVisibleChild();
            return;
        }

        if (e.PropertyName is nameof(ConnectionSetupViewModel.IsBusy)
            or nameof(BucketBrowserViewModel.IsBusy))
        {
            RaisePropertyChanged(nameof(IsBusy));
            _showBucketCommand.RaiseCanExecuteChanged();
        }
    }

    private void SyncStatusFromVisibleChild()
    {
        StatusMessage = IsBucketViewVisible ? Bucket.StatusMessage : Setup.StatusMessage;
        DebugLog.Write($"SyncStatusFromVisibleChild bucketVisible={IsBucketViewVisible}");
    }

    private void HandleUnexpectedCommandException(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            StatusMessage = "Canceled.";
            return;
        }

        DebugLog.Write($"Unexpected main window command error: {ex}");
        StatusMessage = "Unexpected error.";
    }
}
