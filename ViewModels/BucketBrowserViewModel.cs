using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using DropAndForget.Models;
using DropAndForget.Services.BucketContent;
using DropAndForget.Services.Diagnostics;
using DropAndForget.Services.MainWindow;
using DropAndForget.Services.Sync;
using DropAndForget.UI;

namespace DropAndForget.ViewModels;

public sealed class BucketBrowserViewModel : ViewModelBase
{
    private readonly ConnectionSetupViewModel _setup;
    private readonly MainWindowBucketActionService _bucketActionService;
    private readonly BucketContentService _bucketContentService;
    private readonly MainWindowConnectionService _connectionService;
    private readonly IStorageModeCoordinator _storageModeCoordinator;
    private readonly ILocalSyncBrowser _localSyncBrowser;
    private readonly BucketPresentationState _bucketState = new();
    private readonly AsyncRelayCommand _refreshBucketCommand;
    private readonly RelayCommand _deleteSelectedItemCommand;
    private readonly RelayCommand<BucketListEntry> _deleteBucketItemCommand;
    private readonly AsyncRelayCommand _unlockEncryptedBucketCommand;
    private readonly RelayCommand _lockEncryptedBucketCommand;
    private readonly AsyncRelayCommand _changePassphraseCommand;
    private readonly AsyncRelayCommand _openSelectedFolderCommand;
    private readonly AsyncRelayCommand _goUpCommand;
    private readonly AsyncRelayCommand<string> _navigateToBreadcrumbCommand;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly List<BucketListEntry> _selectedBucketItems = [];
    private readonly CancellationTokenSource _shutdownCts = new();
    private string _confirmNewPassphrase = string.Empty;
    private string _currentPrefix = string.Empty;
    private bool _isBusy;
    private bool _isEncryptedLocked;
    private string _newPassphrase = string.Empty;
    private string _searchText = string.Empty;
    private BucketListEntry? _selectedBucketItem;
    private string _statusMessage = string.Empty;
    private string _unlockPassphrase = string.Empty;
    private CancellationTokenSource? _searchCts;

    public BucketBrowserViewModel(
        ConnectionSetupViewModel setup,
        MainWindowBucketActionService bucketActionService,
        BucketContentService bucketContentService,
        MainWindowConnectionService connectionService,
        IStorageModeCoordinator storageModeCoordinator,
        ILocalSyncBrowser localSyncBrowser)
    {
        _setup = setup;
        _bucketActionService = bucketActionService;
        _bucketContentService = bucketContentService;
        _connectionService = connectionService;
        _storageModeCoordinator = storageModeCoordinator;
        _localSyncBrowser = localSyncBrowser;

        _refreshBucketCommand = new AsyncRelayCommand(RefreshBucketAsync, CanRefreshBucket, HandleUnexpectedCommandException);
        RefreshBucketCommand = _refreshBucketCommand;

        _deleteSelectedItemCommand = new RelayCommand(DeleteSelectedItem, CanDeleteSelectedItem);
        DeleteSelectedItemCommand = _deleteSelectedItemCommand;

        _deleteBucketItemCommand = new RelayCommand<BucketListEntry>(DeleteBucketItem, CanDeleteBucketItem);
        DeleteBucketItemCommand = _deleteBucketItemCommand;

        _unlockEncryptedBucketCommand = new AsyncRelayCommand(UnlockEncryptedBucketAsync, CanUnlockEncryptedBucket, HandleUnexpectedCommandException);
        UnlockEncryptedBucketCommand = _unlockEncryptedBucketCommand;

        _lockEncryptedBucketCommand = new RelayCommand(LockEncryptedBucket, CanLockEncryptedBucket);
        LockEncryptedBucketCommand = _lockEncryptedBucketCommand;

        _changePassphraseCommand = new AsyncRelayCommand(ChangePassphraseAsync, CanChangePassphrase, HandleUnexpectedCommandException);
        ChangePassphraseCommand = _changePassphraseCommand;

        _openSelectedFolderCommand = new AsyncRelayCommand(OpenSelectedFolderAsync, CanOpenSelectedFolder, HandleUnexpectedCommandException);
        OpenSelectedFolderCommand = _openSelectedFolderCommand;

        _goUpCommand = new AsyncRelayCommand(GoUpAsync, CanGoUp, HandleUnexpectedCommandException);
        GoUpCommand = _goUpCommand;

        _navigateToBreadcrumbCommand = new AsyncRelayCommand<string>(NavigateToBreadcrumbAsync, CanNavigateToBreadcrumb, HandleUnexpectedCommandException);
        NavigateToBreadcrumbCommand = _navigateToBreadcrumbCommand;

        DialogManager = new DialogManager();
        StatusMessage = "Bucket view ready.";

        _storageModeCoordinator.StatusChanged += OnSyncStatusChanged;
        _storageModeCoordinator.Changed += OnSyncChanged;
        _setup.PropertyChanged += OnSetupPropertyChanged;
    }

    public event Action<string>? SetupRequested;

    public ObservableCollection<BucketListEntry> BucketItems => _bucketState.BucketItems;

    public ObservableCollection<BreadcrumbItem> Breadcrumbs => _bucketState.Breadcrumbs;

    public DialogManager DialogManager { get; }

    public bool IsSyncMode => _setup.IsSyncMode;

    public bool IsEncryptionEnabled => _setup.IsEncryptionEnabled;

    public bool EncryptionBootstrapCompleted => _setup.EncryptionBootstrapCompleted;

    public bool IsEncryptedLocked
    {
        get => _isEncryptedLocked;
        private set
        {
            if (SetProperty(ref _isEncryptedLocked, value))
            {
                RaisePropertyChanges(nameof(IsEncryptedUnlocked), nameof(SyncFooterText));
                RaiseCommandStates();
            }
        }
    }

    public bool IsEncryptedUnlocked => IsEncryptionEnabled && EncryptionBootstrapCompleted && !IsEncryptedLocked;

    public string EncryptionLossesText => _setup.EncryptionLossesText;

    public string UnlockHelpText => "Unlock decrypts the private index in memory only. Passphrase is never saved locally.";

    public string UnlockPassphrase
    {
        get => _unlockPassphrase;
        set
        {
            if (SetProperty(ref _unlockPassphrase, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string NewPassphrase
    {
        get => _newPassphrase;
        set
        {
            if (SetProperty(ref _newPassphrase, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string ConfirmNewPassphrase
    {
        get => _confirmNewPassphrase;
        set
        {
            if (SetProperty(ref _confirmNewPassphrase, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ScheduleSearchUpdate();
                RaisePropertyChanges(nameof(IsBucketWideSearch), nameof(SearchWatermark));
            }
        }
    }

    public BucketListEntry? SelectedBucketItem
    {
        get => _selectedBucketItem;
        set
        {
            if (SetProperty(ref _selectedBucketItem, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public IReadOnlyList<BucketListEntry> SelectedBucketItems => _selectedBucketItems;

    public int SelectedBucketItemCount => _selectedBucketItems.Count;

    public string SyncStatus => _setup.SyncStatus;

    public string SyncFooterText
    {
        get
        {
            if (IsEncryptionEnabled)
            {
                return IsEncryptedLocked ? "Encrypted - locked" : "Encrypted - unlocked";
            }

            if (!IsSyncMode)
            {
                return "Cloud mode";
            }

            if (BucketItems.Any(item => item.IsSyncing))
            {
                return "Syncing";
            }

            if (BucketItems.Any(item => item.IsSyncPending))
            {
                return "Pending changes";
            }

            return "All synced";
        }
    }

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

    public bool IsBucketWideSearch => !string.IsNullOrWhiteSpace(SearchText);

    public int VisibleFolderCount => _bucketState.VisibleFolderCount;

    public int VisibleFileCount => _bucketState.VisibleFileCount;

    public string VisibleTotalSizeText => _bucketState.VisibleTotalSizeText;

    public string SearchWatermark => "Search whole bucket...";

    public string BucketSummary => _setup.BucketSummary;

    public string EndpointSummary => _setup.EndpointSummary;

    public ICommand RefreshBucketCommand { get; }

    public ICommand DeleteSelectedItemCommand { get; }

    public ICommand DeleteBucketItemCommand { get; }

    public ICommand UnlockEncryptedBucketCommand { get; }

    public ICommand LockEncryptedBucketCommand { get; }

    public ICommand ChangePassphraseCommand { get; }

    public ICommand OpenSelectedFolderCommand { get; }

    public ICommand GoUpCommand { get; }

    public ICommand NavigateToBreadcrumbCommand { get; }

    private IReadOnlyCollection<BucketItem> VisibleBucketItems => BucketItems.Select(entry => entry.Item).ToArray();

    public void InitializeFromSetup()
    {
        IsEncryptedLocked = IsEncryptionEnabled && EncryptionBootstrapCompleted;
    }

    public async Task HandleConnectionReadyAsync()
    {
        DebugLog.Write($"HandleConnectionReadyAsync start encrypted={IsEncryptionEnabled} bootstrap={EncryptionBootstrapCompleted} unlocked={IsEncryptedUnlocked}");
        _currentPrefix = string.Empty;
        if (IsEncryptionEnabled && EncryptionBootstrapCompleted && !IsEncryptedUnlocked)
        {
            DebugLog.Write("HandleConnectionReadyAsync leaving bucket locked");
            IsEncryptedLocked = true;
            StatusMessage = "Connected. Encrypted bucket locked.";
            ReplaceBucketItems([]);
            return;
        }

        IsEncryptedLocked = false;
        StatusMessage = "Connected. Loading bucket...";
        await RefreshBucketAsync(runRemoteReconcile: false);
        DebugLog.Write($"HandleConnectionReadyAsync done items={BucketItems.Count}");
    }

    public async Task TryOpenSavedBucketAsync()
    {
        if (!_setup.HasConfiguredConnection)
        {
            return;
        }

        try
        {
            await RefreshSavedConnectionStateAsync();
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            SetupRequested?.Invoke(ex.Message);
        }
    }

    public async Task EnsureBucketShownAsync()
    {
        StatusMessage = "Back to bucket.";

        if (IsEncryptionEnabled && IsEncryptedLocked)
        {
            ReplaceBucketItems([]);
            return;
        }

        if (BucketItems.Count == 0 && !IsBusy && _setup.HasConfiguredConnection)
        {
            await RefreshBucketAsync(runRemoteReconcile: false);
        }
    }

    public async Task HandleDroppedFilesAsync(IReadOnlyList<string> paths)
    {
        if (!_setup.HasConfiguredConnection)
        {
            StatusMessage = "Connect bucket first.";
            return;
        }

        if (IsBusy)
        {
            StatusMessage = "Already working. Wait a sec.";
            return;
        }

        var dropped = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        if (dropped.Count == 0)
        {
            return;
        }

        var filesToUpload = BucketUiHelpers.ExpandDroppedPaths(dropped);
        if (filesToUpload.Count == 0)
        {
            StatusMessage = "Nothing to upload.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Adding {filesToUpload.Count} file{(filesToUpload.Count == 1 ? string.Empty : "s") }...";
            await _bucketActionService.UploadDroppedFilesAsync(CreateConfig(), dropped, _currentPrefix, IsSyncMode, IsEncryptionEnabled, SetStatus, _shutdownCts.Token);

            await RefreshBucketAsync(runRemoteReconcile: false);
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Upload failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Unexpected upload failure: {ex}");
            StatusMessage = "Unexpected upload error.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetStatus(string message)
    {
        StatusMessage = message;
    }

    public bool CanCreateFolderHere() => CanCreateFolder();

    public void SetSelectedBucketItems(IEnumerable<BucketListEntry?> items)
    {
        var next = items
            .Where(static item => item is not null)
            .Cast<BucketListEntry>()
            .Distinct()
            .ToList();

        if (_selectedBucketItems.SequenceEqual(next))
        {
            return;
        }

        _selectedBucketItems.Clear();
        _selectedBucketItems.AddRange(next);
        RaisePropertyChanges(nameof(SelectedBucketItems), nameof(SelectedBucketItemCount));
        RaiseCommandStates();
    }

    public IReadOnlyList<BucketListEntry> GetEffectiveSelectedBucketItems(BucketListEntry? fallbackItem = null)
    {
        var selection = _selectedBucketItems.Count > 0
            ? _selectedBucketItems
            : fallbackItem is null ? [] : [fallbackItem];

        return selection
            .OrderBy(item => item.Key.Count(static c => c == '/'))
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Where(item => !selection.Any(candidate => !ReferenceEquals(candidate, item)
                && candidate.IsFolder
                && IsAncestorOf(candidate.Key, item.Key)))
            .ToList();
    }

    public bool IsItemSelected(BucketListEntry? item)
    {
        return item is not null && _selectedBucketItems.Contains(item);
    }

    public bool CanRenameItem(BucketListEntry? item)
    {
        return CanDeleteBucketItem(item)
            && item?.IsNewPlaceholder != true
            && !IsBucketWideSearch;
    }

    public bool CanDownloadItem(BucketListEntry? item)
    {
        return CanRefreshBucket() && item?.IsFile == true;
    }

    public bool CanDownloadSelectionAsZip(IReadOnlyCollection<BucketListEntry>? items)
    {
        return CanRefreshBucket()
            && items is not null
            && items.Count > 1
            && items.All(item => item.IsNewPlaceholder != true);
    }

    public bool CanDownloadFolderAsZip(BucketListEntry? item)
    {
        return CanRefreshBucket() && item?.IsFolder == true && item.IsNewPlaceholder != true;
    }

    public bool CanStartDrag(BucketListEntry? item)
    {
        return CanRefreshBucket()
            && item is not null
            && item.IsNewPlaceholder != true
            && !item.IsEditing
             && !IsBucketWideSearch;
    }

    public bool CanStartDrag(IReadOnlyCollection<BucketListEntry>? items)
    {
        return items is not null && items.Count > 0 && items.All(CanStartDrag);
    }

    public bool CanMoveItem(BucketListEntry? item, BucketListEntry? targetFolder)
    {
        if (item is null
            || !CanStartDrag(item)
            || targetFolder?.IsFolder != true
            || targetFolder.IsNewPlaceholder
            || targetFolder.IsEditing)
        {
            return false;
        }

        return CanMoveItemToPath(item, targetFolder.Key);
    }

    public bool CanMoveItems(IReadOnlyCollection<BucketListEntry>? items, BucketListEntry? targetFolder)
    {
        return targetFolder?.IsFolder == true
            && !targetFolder.IsNewPlaceholder
            && !targetFolder.IsEditing
            && CanMoveItemsToPath(items, targetFolder.Key);
    }

    public bool CanMoveItemToPath(BucketListEntry? item, string targetFolderPath)
    {
        if (item is null || !CanStartDrag(item))
        {
            return false;
        }

        var sourcePath = NormalizePath(item.Key);
        targetFolderPath = NormalizePath(targetFolderPath);
        if (string.Equals(sourcePath, targetFolderPath, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(NormalizePath(item.FolderPath), targetFolderPath, StringComparison.Ordinal))
        {
            return false;
        }

        return !item.IsFolder || !targetFolderPath.StartsWith(sourcePath + "/", StringComparison.Ordinal);
    }

    public bool CanMoveItemsToPath(IReadOnlyCollection<BucketListEntry>? items, string targetFolderPath)
    {
        if (items is null || items.Count == 0 || !CanStartDrag(items))
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!names.Add(item.DisplayName) || !CanMoveItemToPath(item, targetFolderPath))
            {
                return false;
            }
        }

        return true;
    }

    public BucketListEntry? FindVisibleItem(string key)
    {
        return BucketItems.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
    }

    public bool CanPreviewItem(BucketListEntry? item)
    {
        return CanRefreshBucket() && item?.IsFile == true && item.CanPreview;
    }

    public void BeginNewFolder()
    {
        if (!CanCreateFolder())
        {
            return;
        }

        CancelRename();
        var item = new BucketListEntry(new BucketItem
        {
            DisplayName = string.Empty,
            Detail = "folder",
            FolderPath = _currentPrefix.TrimEnd('/'),
            IsFolder = true,
            SizeText = "--",
            ModifiedText = "--"
        })
        {
            EditName = BucketUiHelpers.BuildNewFolderName(BucketItems.Select(entry => entry.Item)),
            IsEditing = true,
            IsNewPlaceholder = true
        };

        UpdateBucketPresentation(selectedItem => _bucketState.AddPlaceholder(item, SearchText, selectedItem));
        RefreshVisibleState();
    }

    public void BeginRename(BucketListEntry item)
    {
        if (!CanRenameItem(item))
        {
            return;
        }

        CancelRename();
        SelectedBucketItem = item;
        item.EditName = item.DisplayName;
        item.IsEditing = true;
        RefreshBucketFilterIfNeeded();
    }

    public void CancelRename(BucketListEntry? item = null)
    {
        var editingItem = _bucketState.FindEditingItem(item);
        if (editingItem is null)
        {
            return;
        }

        UpdateBucketPresentation(selectedItem => _bucketState.CancelEdit(editingItem, SearchText, selectedItem));
        RefreshVisibleState();
    }

    public async Task CreateFolderAsync(string folderName)
    {
        if (!CanCreateFolder())
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"Creating folder {folderName}...";

        try
        {
            StatusMessage = await _bucketActionService.CreateFolderAsync(CreateConfig(), folderName, VisibleBucketItems, _currentPrefix, _shutdownCts.Token);
            await RefreshBucketAsync(runRemoteReconcile: false);
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Create folder failed for {folderName}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> RenameItemAsync(BucketListEntry item, string newName)
    {
        if (item.IsNewPlaceholder)
        {
            return await CreatePendingFolderAsync(item, newName);
        }

        if (!CanRenameItem(item))
        {
            return false;
        }

        IsBusy = true;
        StatusMessage = item.IsFolder
            ? $"Renaming folder {item.DisplayName}..."
            : $"Renaming file {item.DisplayName}...";

        try
        {
            var result = await _bucketActionService.RenameAsync(CreateConfig(), item, newName, VisibleBucketItems, _shutdownCts.Token);
            if (!result.RequiresRefresh)
            {
                item.IsEditing = false;
                RefreshBucketFilterIfNeeded();
                StatusMessage = result.StatusMessage;
                return result.Success;
            }

            SelectedBucketItem = null;
            await RefreshBucketAsync(runRemoteReconcile: false);
            StatusMessage = result.StatusMessage;
            return result.Success;
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Rename failed for {item.DisplayName}: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DownloadItemAsync(BucketListEntry item, System.IO.Stream destination)
    {
        if (!CanDownloadItem(item))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"Downloading {item.DisplayName}...";

        try
        {
            StatusMessage = await _bucketActionService.DownloadFileAsync(CreateConfig(), item, destination, _shutdownCts.Token);
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Download failed for {item.DisplayName}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DownloadFolderAsZipAsync(BucketListEntry item, System.IO.Stream destination)
    {
        if (!CanDownloadFolderAsZip(item))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"Zipping {item.DisplayName}...";

        try
        {
            StatusMessage = await _bucketActionService.DownloadFolderAsZipAsync(CreateConfig(), item, destination, _shutdownCts.Token);
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Zip download failed for {item.DisplayName}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task MoveItemAsync(BucketListEntry item, BucketListEntry targetFolder)
    {
        if (!CanMoveItem(item, targetFolder))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = item.IsFolder
            ? $"Moving folder {item.DisplayName}..."
            : $"Moving file {item.DisplayName}...";

        try
        {
            StatusMessage = await _bucketActionService.MoveAsync(CreateConfig(), item, targetFolder, _shutdownCts.Token);
            SelectedBucketItem = null;
            await RefreshBucketAsync(runRemoteReconcile: false);
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Move failed for {item.DisplayName}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task MoveItemAsync(BucketListEntry item, string targetFolderPath, string targetLabel)
    {
        if (!CanMoveItemToPath(item, targetFolderPath))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = item.IsFolder
            ? $"Moving folder {item.DisplayName}..."
            : $"Moving file {item.DisplayName}...";

        try
        {
            StatusMessage = await _bucketActionService.MoveAsync(CreateConfig(), item, targetFolderPath, targetLabel, _shutdownCts.Token);
            SelectedBucketItem = null;
            await RefreshBucketAsync(runRemoteReconcile: false);
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Move failed for {item.DisplayName}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<FilePreviewData?> LoadPreviewAsync(BucketListEntry item)
    {
        if (!CanPreviewItem(item))
        {
            StatusMessage = $"Preview not available for {item.DisplayName}.";
            return null;
        }

        IsBusy = true;
        StatusMessage = $"Loading preview for {item.DisplayName}...";

        try
        {
            var result = await _bucketActionService.LoadPreviewAsync(CreateConfig(), item, _shutdownCts.Token);
            StatusMessage = result.StatusMessage;
            return result.Preview;
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Preview failed for {item.DisplayName}: {ex.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenFolderAsync(BucketListEntry? item)
    {
        if (!TryOpenFolder(item, IsBusy, out var nextPrefix))
        {
            return;
        }

        _currentPrefix = nextPrefix;
        await RefreshBucketAsync(runRemoteReconcile: false);
    }

    public async Task StopSyncAsync()
    {
        CancelPendingSearch();
        if (!_shutdownCts.IsCancellationRequested)
        {
            _shutdownCts.Cancel();
        }

        await _storageModeCoordinator.StopAsync();
        _connectionService.Lock();
        _storageModeCoordinator.StatusChanged -= OnSyncStatusChanged;
        _storageModeCoordinator.Changed -= OnSyncChanged;
        _setup.PropertyChanged -= OnSetupPropertyChanged;
    }

    private bool CanRefreshBucket()
    {
        return _setup.HasConfiguredConnection && !IsBusy && (!IsEncryptionEnabled || IsEncryptedUnlocked);
    }

    private bool CanDeleteSelectedItem() => CanRefreshBucket() && SelectedBucketItemCount > 0;

    private bool CanDeleteBucketItem(BucketListEntry? item) => CanRefreshBucket() && item is not null;

    private bool CanCreateFolder() => CanRefreshBucket();

    private bool CanOpenSelectedFolder() => CanRefreshBucket() && SelectedBucketItemCount == 1 && SelectedBucketItem?.IsFolder == true;

    private bool CanGoUp() => CanRefreshBucket() && !string.IsNullOrEmpty(_currentPrefix);

    private bool CanNavigateToBreadcrumb(string? prefix) => CanRefreshBucket() && prefix is not null;

    private bool CanUnlockEncryptedBucket()
    {
        return IsEncryptionEnabled
            && EncryptionBootstrapCompleted
            && IsEncryptedLocked
            && !IsBusy
            && !string.IsNullOrWhiteSpace(UnlockPassphrase);
    }

    private bool CanLockEncryptedBucket() => IsEncryptionEnabled && EncryptionBootstrapCompleted && !IsEncryptedLocked && !IsBusy;

    private bool CanChangePassphrase()
    {
        return IsEncryptionEnabled
            && IsEncryptedUnlocked
            && !IsBusy
            && !string.IsNullOrWhiteSpace(NewPassphrase)
            && string.Equals(NewPassphrase, ConfirmNewPassphrase, StringComparison.Ordinal);
    }

    private AppConfig CreateConfig() => _setup.CreateConfig();

    private async Task UnlockEncryptedBucketAsync()
    {
        IsBusy = true;
        StatusMessage = "Unlocking encrypted bucket...";

        try
        {
            StatusMessage = await _connectionService.UnlockAsync(CreateConfig(), UnlockPassphrase, _shutdownCts.Token);
            IsEncryptedLocked = false;
            UnlockPassphrase = string.Empty;
            _currentPrefix = string.Empty;
            await RefreshBucketAsync(runRemoteReconcile: false);
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

    private void LockEncryptedBucket()
    {
        _connectionService.Lock();
        ReplaceBucketItems([]);
        SearchText = string.Empty;
        _currentPrefix = string.Empty;
        IsEncryptedLocked = true;
        StatusMessage = "Encrypted bucket locked.";
    }

    private async Task ChangePassphraseAsync()
    {
        IsBusy = true;
        StatusMessage = "Changing passphrase...";

        try
        {
            StatusMessage = await _connectionService.ChangePassphraseAsync(CreateConfig(), UnlockPassphrase, NewPassphrase, ConfirmNewPassphrase, _shutdownCts.Token);
            NewPassphrase = string.Empty;
            ConfirmNewPassphrase = string.Empty;
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

    private void DeleteSelectedItem()
    {
        if (CanDeleteSelectedItem())
        {
            ConfirmDeleteItems(GetEffectiveSelectedBucketItems(SelectedBucketItem));
        }
    }

    private void DeleteBucketItem(BucketListEntry? item)
    {
        if (item is not null && CanDeleteBucketItem(item))
        {
            ConfirmDeleteItems(GetEffectiveSelectedBucketItems(item));
        }
    }

    private async Task DeleteItemAsync(BucketListEntry item)
    {
        if (!CanDeleteBucketItem(item))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = item.IsFolder ? $"Deleting folder {item.DisplayName}..." : $"Deleting file {item.DisplayName}...";

        try
        {
            var statusMessage = await _bucketActionService.DeleteAsync(CreateConfig(), item, _shutdownCts.Token);
            SelectedBucketItem = null;
            await RefreshBucketAsync(runRemoteReconcile: false);
            StatusMessage = statusMessage;
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Delete failed for {item.DisplayName}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteItemsAsync(IReadOnlyList<BucketListEntry> items)
    {
        if (items.Count == 0 || !items.All(CanDeleteBucketItem))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"Deleting {items.Count} item{(items.Count == 1 ? string.Empty : "s")}...";

        try
        {
            var statusMessage = await _bucketActionService.DeleteAsync(CreateConfig(), items, _shutdownCts.Token);
            ClearSelection();
            await RefreshBucketAsync(runRemoteReconcile: false);
            StatusMessage = statusMessage;
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Delete failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ConfirmDeleteItems(IReadOnlyList<BucketListEntry> items)
    {
        if (items.Count == 0 || !items.All(CanDeleteBucketItem))
        {
            return;
        }

        CancelRename();
        var item = items[0];
        SelectedBucketItem = item;
        DialogManager
            .CreateDialog(
                items.Count == 1
                    ? item.IsFolder ? "Delete folder?" : "Delete file?"
                    : "Delete items?",
                items.Count == 1
                    ? item.IsFolder
                        ? $"Delete {item.DisplayName} and everything inside it? This can't be undone."
                        : $"Delete {item.DisplayName}? This can't be undone."
                    : $"Delete {items.Count} selected items? This can't be undone.")
            .WithPrimaryButton("Delete", () => items.Count == 1 ? DeleteItemAsync(item) : DeleteItemsAsync(items), DialogButtonStyle.Destructive)
            .WithCancelButton("Cancel")
            .WithMaxWidth(420)
            .Dismissible()
            .Show();
    }

    private async Task GoUpAsync()
    {
        if (!TryGoUp(_currentPrefix, out var nextPrefix))
        {
            return;
        }

        _currentPrefix = nextPrefix;
        await RefreshBucketAsync(runRemoteReconcile: false);
    }

    private async Task NavigateToBreadcrumbAsync(string? prefix)
    {
        if (!TryNavigateToBreadcrumb(prefix, out var nextPrefix))
        {
            return;
        }

        _currentPrefix = nextPrefix;
        await RefreshBucketAsync(runRemoteReconcile: false);
    }

    private Task RefreshBucketAsync() => RefreshBucketAsync(runRemoteReconcile: true);

    private async Task RefreshBucketAsync(bool runRemoteReconcile = false)
    {
        DebugLog.Write($"RefreshBucketAsync start encrypted={IsEncryptionEnabled} locked={IsEncryptedLocked} sync={IsSyncMode} configured={_setup.HasConfiguredConnection}");
        if (!_setup.HasConfiguredConnection)
        {
            DebugLog.Write("RefreshBucketAsync aborted: no configured connection");
            return;
        }

        if (IsEncryptionEnabled && IsEncryptedLocked)
        {
            DebugLog.Write("RefreshBucketAsync aborted: encrypted and locked");
            ReplaceBucketItems([]);
            StatusMessage = "Encrypted bucket locked. Enter passphrase.";
            return;
        }

        await _refreshLock.WaitAsync();
        IsBusy = true;
        StatusMessage = "Loading bucket...";

        try
        {
            CancelPendingSearch();
            var items = await ListBucketEntriesAsync(runRemoteReconcile, _shutdownCts.Token);
            DebugLog.Write($"RefreshBucketAsync loaded {items.Count} items");
            ReplaceBucketItems(items);
            if (IsBucketWideSearch)
            {
                await UpdateSearchAsync(_shutdownCts.Token);
            }
            else
            {
                StatusMessage = $"Loaded {items.Count} entr{(items.Count == 1 ? "y" : "ies")}.";
            }
            DebugLog.Write($"RefreshBucketAsync success visible={BucketItems.Count}");
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            DebugLog.Write($"RefreshBucketAsync handled failure: {ex}");
            SetupRequested?.Invoke(ex.Message);
        }
        finally
        {
            IsBusy = false;
            _refreshLock.Release();
        }
    }

    private void ReplaceBucketItems(IEnumerable<BucketListEntry> items)
    {
        UpdateBucketPresentation(_ => _bucketState.ReplaceItems(items, _currentPrefix, SearchText, null));
        DebugLog.Write($"ReplaceBucketItems applied count={BucketItems.Count}");
        RefreshVisibleState();
    }

    private void ApplyBucketFilter()
    {
        UpdateBucketPresentation(selectedItem => _bucketState.RefreshFilter(SearchText, selectedItem));
        RefreshVisibleState();
    }

    private async Task UpdateSearchAsync(CancellationToken cancellationToken)
    {
        var term = SearchText.Trim();
        var requestId = _bucketState.NextSearchRequestId();

        if (string.IsNullOrWhiteSpace(term) || !_setup.HasConfiguredConnection || (IsEncryptionEnabled && IsEncryptedLocked))
        {
            UpdateBucketPresentation(selectedItem => _bucketState.ClearSearchResults(SearchText, selectedItem));
            RefreshVisibleState();
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            StatusMessage = $"Searching bucket for \"{term}\"...";
            var results = await SearchBucketEntriesAsync(term, cancellationToken);
            if (!_bucketState.IsSearchRequestCurrent(requestId))
            {
                return;
            }

            UpdateBucketPresentation(selectedItem => _bucketState.ReplaceSearchResults(results, SearchText, selectedItem));
            RefreshVisibleState();
            StatusMessage = $"Found {results.Count} result{(results.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            if (!_bucketState.IsSearchRequestCurrent(requestId))
            {
                return;
            }

            UpdateBucketPresentation(selectedItem => _bucketState.ClearSearchResults(SearchText, selectedItem));
            RefreshVisibleState();
            StatusMessage = $"Search failed: {ex.Message}";
        }
    }

    private void UpdateBucketPresentation(Func<BucketListEntry?, BucketListEntry?> update)
    {
        var selectedItem = SelectedBucketItem;
        SetSelectedBucketItems([]);
        SelectedBucketItem = null;
        SelectedBucketItem = update(selectedItem);
    }

    public async Task DownloadItemsAsZipAsync(IReadOnlyList<BucketListEntry> items, System.IO.Stream destination)
    {
        if (!CanDownloadSelectionAsZip(items))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"Zipping {items.Count} items...";

        try
        {
            StatusMessage = await _bucketActionService.DownloadAsZipAsync(CreateConfig(), items, destination, _shutdownCts.Token);
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Zip download failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task MoveItemsAsync(IReadOnlyList<BucketListEntry> items, BucketListEntry targetFolder)
    {
        if (!CanMoveItems(items, targetFolder))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"Moving {items.Count} item{(items.Count == 1 ? string.Empty : "s")}...";

        try
        {
            StatusMessage = await _bucketActionService.MoveAsync(CreateConfig(), items, targetFolder, _shutdownCts.Token);
            ClearSelection();
            await RefreshBucketAsync(runRemoteReconcile: false);
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Move failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task MoveItemsAsync(IReadOnlyList<BucketListEntry> items, string targetFolderPath, string targetLabel)
    {
        if (!CanMoveItemsToPath(items, targetFolderPath))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"Moving {items.Count} item{(items.Count == 1 ? string.Empty : "s")}...";

        try
        {
            StatusMessage = await _bucketActionService.MoveAsync(CreateConfig(), items, targetFolderPath, targetLabel, _shutdownCts.Token);
            ClearSelection();
            await RefreshBucketAsync(runRemoteReconcile: false);
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Move failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshBucketFilterIfNeeded()
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            ApplyBucketFilter();
        }
    }

    private void RaiseBucketStats()
    {
        RaisePropertyChanges(nameof(VisibleFolderCount), nameof(VisibleFileCount), nameof(VisibleTotalSizeText));
    }

    private void RefreshVisibleState()
    {
        RaiseBucketStats();
        RaiseCommandStates();
        RaisePropertyChanged(nameof(SyncFooterText));
    }

    private async Task OpenSelectedFolderAsync()
    {
        await OpenFolderAsync(SelectedBucketItem);
    }

    private void ClearSelection()
    {
        SetSelectedBucketItems([]);
        SelectedBucketItem = null;
    }

    private static bool IsAncestorOf(string parentKey, string childKey)
    {
        var normalizedParent = NormalizePath(parentKey);
        var normalizedChild = NormalizePath(childKey);
        return normalizedChild.StartsWith(normalizedParent + "/", StringComparison.Ordinal);
    }

    private async Task RefreshSavedConnectionStateAsync()
    {
        var config = await _connectionService.RefreshSavedRemoteStateAsync(CreateConfig(), _shutdownCts.Token);
        _setup.ApplyConfig(config);
        _currentPrefix = string.Empty;
        if (IsEncryptionEnabled && EncryptionBootstrapCompleted)
        {
            ReplaceBucketItems([]);
            StatusMessage = "Encrypted bucket locked. Enter passphrase.";
            IsEncryptedLocked = true;
            return;
        }

        StatusMessage = "Loading saved bucket...";
        await RefreshBucketAsync(runRemoteReconcile: false);
    }

    private async Task<IReadOnlyList<BucketListEntry>> ListBucketEntriesAsync(bool runRemoteReconcile, CancellationToken cancellationToken)
    {
        if (IsSyncMode && runRemoteReconcile)
        {
            await _storageModeCoordinator.ReconcileNowAsync(cancellationToken);
        }

        var items = await _bucketContentService.ListAsync(CreateConfig(), _currentPrefix, cancellationToken);
        return CreateBucketEntries(items);
    }

    private async Task<IReadOnlyList<BucketListEntry>> SearchBucketEntriesAsync(string term, CancellationToken cancellationToken)
    {
        var items = await _bucketContentService.SearchAsync(CreateConfig(), term, cancellationToken);
        return CreateBucketEntries(items);
    }

    private void ScheduleSearchUpdate()
    {
        CancelPendingSearch();
        var searchCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
        _searchCts = searchCts;
        MainWindowUiSupport.ObserveBackgroundTask(DebounceSearchAsync(searchCts), "search bucket");
    }

    private async Task DebounceSearchAsync(CancellationTokenSource searchCts)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), searchCts.Token);
            await UpdateSearchAsync(searchCts.Token);
        }
        finally
        {
            if (ReferenceEquals(_searchCts, searchCts))
            {
                _searchCts = null;
            }

            searchCts.Dispose();
        }
    }

    private void CancelPendingSearch()
    {
        var searchCts = _searchCts;
        if (searchCts is null)
        {
            return;
        }

        _searchCts = null;
        searchCts.Cancel();
        searchCts.Dispose();
    }

    private IReadOnlyList<BucketListEntry> CreateBucketEntries(IEnumerable<BucketItem> items)
    {
        return items
            .Select(item =>
            {
                var entry = new BucketListEntry(item)
                {
                    CanPreview = BucketUiHelpers.GetPreviewKind(item) is not null
                };
                BucketUiHelpers.ApplySyncVisualState(entry, IsSyncMode, IsEncryptionEnabled, _storageModeCoordinator);
                return entry;
            })
            .ToList();
    }

    private void RaiseCommandStates()
    {
        _refreshBucketCommand.RaiseCanExecuteChanged();
        _deleteSelectedItemCommand.RaiseCanExecuteChanged();
        _deleteBucketItemCommand.RaiseCanExecuteChanged();
        _unlockEncryptedBucketCommand.RaiseCanExecuteChanged();
        _lockEncryptedBucketCommand.RaiseCanExecuteChanged();
        _changePassphraseCommand.RaiseCanExecuteChanged();
        _openSelectedFolderCommand.RaiseCanExecuteChanged();
        _goUpCommand.RaiseCanExecuteChanged();
        _navigateToBreadcrumbCommand.RaiseCanExecuteChanged();
        RaisePropertyChanges(nameof(BucketSummary), nameof(EndpointSummary), nameof(SyncFooterText));
    }

    private void OnSyncChanged()
    {
        Dispatcher.UIThread.Post(() => MainWindowUiSupport.ObserveBackgroundTask(RefreshBucketAsync(runRemoteReconcile: false), "refresh bucket from sync change"));
    }

    private void OnSyncStatusChanged(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _setup.SetSyncStatus(message);
            RaisePropertyChanges(nameof(SyncStatus), nameof(SyncFooterText));
        });
    }

    private void OnSetupPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ConnectionSetupViewModel.BucketSummary):
                RaisePropertyChanged(nameof(BucketSummary));
                break;
            case nameof(ConnectionSetupViewModel.EndpointSummary):
                RaisePropertyChanged(nameof(EndpointSummary));
                break;
            case nameof(ConnectionSetupViewModel.IsSyncMode):
            case nameof(ConnectionSetupViewModel.IsEncryptionEnabled):
            case nameof(ConnectionSetupViewModel.EncryptionBootstrapCompleted):
                if (!IsEncryptionEnabled || !EncryptionBootstrapCompleted)
                {
                    IsEncryptedLocked = false;
                }
                RaisePropertyChanges(
                    nameof(IsSyncMode),
                    nameof(IsEncryptionEnabled),
                    nameof(EncryptionBootstrapCompleted),
                    nameof(IsEncryptedUnlocked),
                    nameof(SyncFooterText));
                RaiseCommandStates();
                break;
            case nameof(ConnectionSetupViewModel.SyncStatus):
                RaisePropertyChanged(nameof(SyncStatus));
                break;
        }
    }

    private async Task<bool> CreatePendingFolderAsync(BucketListEntry item, string folderName)
    {
        if (!item.IsNewPlaceholder)
        {
            return false;
        }

        IsBusy = true;
        StatusMessage = $"Creating folder {folderName}...";

        try
        {
            var targetPrefix = item.FolderPath;
            var result = await _bucketActionService.CreatePendingFolderAsync(CreateConfig(), item, folderName, VisibleBucketItems, _shutdownCts.Token);
            if (!result.Success)
            {
                StatusMessage = result.StatusMessage;
                return false;
            }

            _currentPrefix = targetPrefix;
            await RefreshBucketAsync(runRemoteReconcile: false);
            StatusMessage = result.StatusMessage;
            return true;
        }
        catch (Exception ex) when (MainWindowUiSupport.IsHandledStatusException(ex))
        {
            StatusMessage = $"Create folder failed for {folderName}: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool TryGoUp(string currentPrefix, out string nextPrefix)
    {
        if (string.IsNullOrEmpty(currentPrefix))
        {
            nextPrefix = currentPrefix;
            return false;
        }

        var trimmed = currentPrefix.TrimEnd('/');
        var slashIndex = trimmed.LastIndexOf('/');
        nextPrefix = slashIndex >= 0 ? trimmed[..(slashIndex + 1)] : string.Empty;
        return true;
    }

    private static bool TryNavigateToBreadcrumb(string? prefix, out string nextPrefix)
    {
        nextPrefix = prefix ?? string.Empty;
        return prefix is not null;
    }

    private static bool TryOpenFolder(BucketListEntry? item, bool isBusy, out string nextPrefix)
    {
        if (item?.IsFolder != true || isBusy)
        {
            nextPrefix = string.Empty;
            return false;
        }

        nextPrefix = item.Key;
        return true;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private void HandleUnexpectedCommandException(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            StatusMessage = "Canceled.";
            return;
        }

        DebugLog.Write($"Unexpected bucket command error: {ex}");
        StatusMessage = "Unexpected error.";
    }
}
