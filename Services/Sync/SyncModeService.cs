using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using DropAndForget.Models;
using DropAndForget.Services.Cloudflare;
using DropAndForget.Services.Diagnostics;

namespace DropAndForget.Services.Sync;

/// <summary>
/// Implements local folder sync against R2.
/// </summary>
public class SyncModeService(
    IR2BucketService bucketService,
    SyncStateStore stateStore,
    TimeSpan? debounceDelay = null,
    TimeSpan? pollInterval = null,
    TimeSpan? suppressionDuration = null) : ISyncModeService
{
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly ConcurrentDictionary<string, int> _pendingVersions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _suppressedUntilUtc = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SyncVisualState> _visualStates = new(StringComparer.Ordinal);
    private Dictionary<string, SyncItemState> _state = new(StringComparer.Ordinal);
    private CancellationTokenSource? _lifetimeCts;
    private FileSystemWatcher? _watcher;
    private Task? _pollTask;
    private AppConfig? _config;

    private readonly IR2BucketService _bucketService = bucketService ?? throw new ArgumentNullException(nameof(bucketService));
    private readonly SyncStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly TimeSpan _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(800);
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromSeconds(30);
    private readonly TimeSpan _suppressionDuration = suppressionDuration ?? TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    public event Action? Changed;

    /// <inheritdoc/>
    public event Action<string>? StatusChanged;

    /// <inheritdoc/>
    public bool IsRunning => _watcher is not null && _lifetimeCts is not null && !_lifetimeCts.IsCancellationRequested;

    /// <inheritdoc/>
    public string CurrentFolderPath => _config is null ? string.Empty : GetSyncFolderPath(_config);

    /// <inheritdoc/>
    public SyncVisualState GetVisualState(string relativePath, bool isFolder = false)
    {
        if (_visualStates.TryGetValue(relativePath, out var state))
        {
            return state;
        }

        return _state.ContainsKey(relativePath)
            ? SyncVisualState.Synced
            : GetAggregateVisualState(relativePath, isFolder);
    }

    /// <inheritdoc/>
    public async Task StartAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        if (config.StorageMode != StorageMode.Sync)
        {
            await StopAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(config.SyncFolderPath))
        {
            throw new InvalidOperationException("Pick a sync folder first.");
        }

        await StopAsync();

        _config = CloneConfig(config);
        var folderPath = GetSyncFolderPath(_config);
        Directory.CreateDirectory(folderPath);

        _state = new Dictionary<string, SyncItemState>(_stateStore.Load(_config), StringComparer.Ordinal);

        await BootstrapIfNeededAsync(_config, cancellationToken);
        await ReconcileRemoteAsync(_config, cancellationToken);

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        StartWatcher(folderPath);
        _pollTask = RunRemotePollLoopAsync(_lifetimeCts.Token);
        PublishStatus("Sync on.");
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        var cts = _lifetimeCts;
        _lifetimeCts = null;

        if (cts is not null)
        {
            cts.Cancel();
        }

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnWatcherChanged;
            _watcher.Changed -= OnWatcherChanged;
            _watcher.Deleted -= OnWatcherChanged;
            _watcher.Renamed -= OnWatcherRenamed;
            _watcher.Dispose();
            _watcher = null;
        }

        if (_pollTask is not null)
        {
            try
            {
                await _pollTask;
            }
            catch (OperationCanceledException)
            {
            }

            _pollTask = null;
        }

        cts?.Dispose();
        _pendingVersions.Clear();
        _suppressedUntilUtc.Clear();
        _visualStates.Clear();
    }

    /// <inheritdoc/>
    public async Task ReconcileNowAsync(CancellationToken cancellationToken = default)
    {
        if (_config is null)
        {
            return;
        }

        await ReconcileRemoteAsync(_config, cancellationToken);
    }

    /// <inheritdoc/>
    public string GetSyncFolderPath(AppConfig config)
    {
        var configured = config.SyncFolderPath.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return ExpandHome(configured);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "DropAndForget", config.BucketName.Trim());
    }

    /// <inheritdoc/>
    public async Task CopyIntoSyncFolderAsync(AppConfig config, IReadOnlyList<string> sourcePaths, string currentPrefix, CancellationToken cancellationToken = default)
    {
        var root = GetSyncFolderPath(config);
        Directory.CreateDirectory(root);

        foreach (var sourcePath in sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(sourcePath))
            {
                var relativePath = CombineRelative(currentPrefix, Path.GetFileName(sourcePath));
                await CopyFileIntoFolderAsync(sourcePath, root, relativePath, cancellationToken);
                continue;
            }

            if (!Directory.Exists(sourcePath))
            {
                continue;
            }

            var directoryName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativeUnderDrop = Path.GetRelativePath(sourcePath, filePath).Replace('\\', '/');
                var relativePath = CombineRelative(currentPrefix, directoryName + "/" + relativeUnderDrop);
                await CopyFileIntoFolderAsync(filePath, root, relativePath, cancellationToken);
            }
        }
    }

    /// <inheritdoc/>
    public async Task DownloadLocalFolderAsZipAsync(string folderPath, Stream destination, CancellationToken cancellationToken = default)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        foreach (var directoryPath in Directory.EnumerateDirectories(folderPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(folderPath, directoryPath).Replace('\\', '/').TrimEnd('/');
            archive.CreateEntry(folderName + "/" + relativePath + "/");
        }

        foreach (var filePath in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(folderPath, filePath).Replace('\\', '/');
            var entry = archive.CreateEntry(folderName + "/" + relativePath, CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await using var fileStream = File.OpenRead(filePath);
            await fileStream.CopyToAsync(entryStream, cancellationToken);
        }
    }

    private async Task BootstrapIfNeededAsync(AppConfig config, CancellationToken cancellationToken)
    {
        var folderPath = GetSyncFolderPath(config);
        var hasLocalEntries = Directory.EnumerateFileSystemEntries(folderPath).Any();
        if (_state.Count > 0)
        {
            return;
        }

        if (hasLocalEntries)
        {
            throw new InvalidOperationException("Sync folder must be empty before first sync.");
        }

        PublishStatus("Downloading remote snapshot...");
        var remoteItems = await _bucketService.ListAllObjectsAsync(config, cancellationToken);

        foreach (var remoteItem in remoteItems.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = ToRelativePath(remoteItem);
            if (string.IsNullOrEmpty(relativePath))
            {
                continue;
            }

            var fullPath = GetFullPath(folderPath, relativePath);
            if (remoteItem.IsFolder)
            {
                Directory.CreateDirectory(fullPath);
            }
            else
            {
                await _bucketService.DownloadObjectToFileAsync(config, remoteItem.Key, fullPath, cancellationToken);
            }

            _state[relativePath] = BuildStateFromPath(relativePath, fullPath, remoteItem);
            _visualStates[relativePath] = SyncVisualState.Synced;
        }

        SaveState(config);
    }

    private void StartWatcher(string folderPath)
    {
        _watcher = new FileSystemWatcher(folderPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.CreationTime
                | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnWatcherChanged;
        _watcher.Changed += OnWatcherChanged;
        _watcher.Deleted += OnWatcherChanged;
        _watcher.Renamed += OnWatcherRenamed;
    }

    private async Task RunRemotePollLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (_config is null)
            {
                continue;
            }

            try
            {
                await ReconcileRemoteAsync(_config, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AmazonS3Exception ex)
            {
                PublishRemotePollFailure(ex);
            }
            catch (IOException ex)
            {
                PublishRemotePollFailure(ex);
            }
            catch (InvalidOperationException ex)
            {
                PublishRemotePollFailure(ex);
            }
        }
    }

    private async Task ReconcileRemoteAsync(AppConfig config, CancellationToken cancellationToken)
    {
        await _syncLock.WaitAsync(cancellationToken);

        try
        {
            var hasChanges = false;
            var folderPath = GetSyncFolderPath(config);
            Directory.CreateDirectory(folderPath);
            var remoteItems = await _bucketService.ListAllObjectsAsync(config, cancellationToken);
            var remoteMap = remoteItems
                .Where(item => !string.IsNullOrWhiteSpace(ToRelativePath(item)))
                .ToDictionary(item => ToRelativePath(item), item => item, StringComparer.Ordinal);

            foreach (var remotePair in remoteMap)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = remotePair.Key;
                var remoteItem = remotePair.Value;
                var fullPath = GetFullPath(folderPath, relativePath);
                _state.TryGetValue(relativePath, out var existingState);

                if (remoteItem.IsFolder)
                {
                    if (!Directory.Exists(fullPath))
                    {
                        SuppressPath(relativePath);
                        Directory.CreateDirectory(fullPath);
                        hasChanges = true;
                    }

                    _state[relativePath] = BuildStateFromPath(relativePath, fullPath, remoteItem);
                    _visualStates[relativePath] = SyncVisualState.Synced;
                    continue;
                }

                var localExists = File.Exists(fullPath);
                var remoteChanged = existingState is null || !string.Equals(existingState.RemoteETag, remoteItem.ETag, StringComparison.Ordinal);

                if (!localExists)
                {
                    await DownloadRemoteFileAsync(config, relativePath, remoteItem, fullPath, cancellationToken);
                    hasChanges = true;
                    continue;
                }

                if (!remoteChanged)
                {
                    _state[relativePath] = BuildStateFromPath(relativePath, fullPath, remoteItem);
                    _visualStates[relativePath] = SyncVisualState.Synced;
                    continue;
                }

                if (existingState is not null && LocalChangedSinceState(fullPath, existingState))
                {
                    existingState.LastConflict = "Local and remote both changed.";
                    _state[relativePath] = existingState;
                    _visualStates[relativePath] = SyncVisualState.Pending;
                    PublishStatus($"Conflict: {relativePath}");
                    continue;
                }

                await DownloadRemoteFileAsync(config, relativePath, remoteItem, fullPath, cancellationToken);
                hasChanges = true;
            }

            foreach (var existingPair in _state.ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (remoteMap.ContainsKey(existingPair.Key))
                {
                    continue;
                }

                var relativePath = existingPair.Key;
                var existingState = existingPair.Value;
                var fullPath = GetFullPath(folderPath, relativePath);

                if (existingState.IsFolder)
                {
                    if (Directory.Exists(fullPath) && !Directory.EnumerateFileSystemEntries(fullPath).Any())
                    {
                        SuppressPath(relativePath);
                        Directory.Delete(fullPath, recursive: false);
                        hasChanges = true;
                    }

                    _state.Remove(relativePath);
                    _visualStates.TryRemove(relativePath, out _);
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    _state.Remove(relativePath);
                    continue;
                }

                if (LocalChangedSinceState(fullPath, existingState))
                {
                    existingState.LastConflict = "Remote deleted file with local edits pending.";
                    _state[relativePath] = existingState;
                    _visualStates[relativePath] = SyncVisualState.Pending;
                    PublishStatus($"Conflict: {relativePath}");
                    continue;
                }

                SuppressPath(relativePath);
                File.Delete(fullPath);
                _state.Remove(relativePath);
                _visualStates.TryRemove(relativePath, out _);
                hasChanges = true;
            }

            SaveState(config);
            if (hasChanges)
            {
                Changed?.Invoke();
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task DownloadRemoteFileAsync(AppConfig config, string relativePath, R2ObjectInfo remoteItem, string fullPath, CancellationToken cancellationToken)
    {
        SuppressPath(relativePath);
        _visualStates[relativePath] = SyncVisualState.Syncing;
        await _bucketService.DownloadObjectToFileAsync(config, remoteItem.Key, fullPath, cancellationToken);
        _state[relativePath] = BuildStateFromPath(relativePath, fullPath, remoteItem);
        _visualStates[relativePath] = SyncVisualState.Synced;
        PublishStatus($"Synced down {relativePath}");
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs e)
    {
        QueueLocalChange(e.FullPath);
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        QueueLocalChange(e.OldFullPath);
        QueueLocalChange(e.FullPath);
    }

    private void QueueLocalChange(string fullPath)
    {
        if (_config is null || _lifetimeCts is null)
        {
            return;
        }

        var relativePath = GetRelativePath(_config, fullPath);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var version = _pendingVersions.AddOrUpdate(relativePath, 1, static (_, current) => current + 1);
        _visualStates[relativePath] = SyncVisualState.Pending;
        ObserveBackgroundTask(DebounceAndProcessAsync(relativePath, version, _lifetimeCts.Token), relativePath);
    }

    private async Task DebounceAndProcessAsync(string relativePath, int version, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_debounceDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_pendingVersions.TryGetValue(relativePath, out var currentVersion) || currentVersion != version)
        {
            return;
        }

        _pendingVersions.TryRemove(relativePath, out _);

        try
        {
            await ProcessLocalChangeAsync(relativePath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            }
            catch (AmazonS3Exception ex)
            {
                PublishProcessLocalChangeFailure(relativePath, ex);
            }
            catch (IOException ex)
            {
                PublishProcessLocalChangeFailure(relativePath, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                PublishProcessLocalChangeFailure(relativePath, ex);
            }
            catch (InvalidOperationException ex)
            {
                PublishProcessLocalChangeFailure(relativePath, ex);
            }
        }

    private async Task ProcessLocalChangeAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (_config is null || IsSuppressed(relativePath))
        {
            return;
        }

        await _syncLock.WaitAsync(cancellationToken);

        try
        {
            var fullPath = GetFullPath(GetSyncFolderPath(_config), relativePath);

            if (Directory.Exists(fullPath))
            {
                _visualStates[relativePath] = SyncVisualState.Syncing;
                var folderName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var prefix = GetParentPrefix(relativePath);
                await _bucketService.CreateFolderAsync(_config, folderName, prefix, cancellationToken);
                var remoteInfo = await LoadCreatedFolderInfoAsync(_config, relativePath, cancellationToken);
                _state[relativePath] = BuildStateFromPath(relativePath, fullPath, remoteInfo);
                _visualStates[relativePath] = SyncVisualState.Synced;
                SaveState(_config);
                Changed?.Invoke();
                return;
            }

            if (File.Exists(fullPath))
            {
                _visualStates[relativePath] = SyncVisualState.Syncing;
                await WaitForFileReadyAsync(fullPath, cancellationToken);
                await _bucketService.UploadFileAsync(_config, fullPath, relativeObjectPath: relativePath, cancellationToken: cancellationToken);
                var remoteInfo = await LoadUploadedFileInfoAsync(_config, relativePath, fullPath, cancellationToken);
                _state[relativePath] = BuildStateFromPath(relativePath, fullPath, remoteInfo);
                _visualStates[relativePath] = SyncVisualState.Synced;
                SaveState(_config);
                PublishStatus($"Synced up {relativePath}");
                Changed?.Invoke();
                return;
            }

            if (_state.TryGetValue(relativePath, out var existingState))
            {
                var objectKey = existingState.IsFolder ? relativePath.TrimEnd('/') + "/" : relativePath;
                _visualStates[relativePath] = SyncVisualState.Syncing;
                await _bucketService.DeleteObjectByKeyAsync(_config, objectKey, cancellationToken);
                _state.Remove(relativePath);
                _visualStates.TryRemove(relativePath, out _);
                SaveState(_config);
                PublishStatus($"Deleted {relativePath}");
                Changed?.Invoke();
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task CopyFileIntoFolderAsync(string sourcePath, string rootPath, string relativePath, CancellationToken cancellationToken)
    {
        var destinationPath = GetFullPath(rootPath, relativePath);
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var source = File.OpenRead(sourcePath);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private void SaveState(AppConfig config)
    {
        _stateStore.Save(config, _state);
    }

    private static SyncItemState BuildStateFromPath(string relativePath, string fullPath, R2ObjectInfo remoteItem)
    {
        var info = remoteItem.IsFolder
            ? null
            : new FileInfo(fullPath);

        return new SyncItemState
        {
            RelativePath = relativePath,
            IsFolder = remoteItem.IsFolder,
            LastKnownLocalSize = info?.Exists == true ? info.Length : null,
            LastKnownLocalWriteUtc = remoteItem.IsFolder
                ? Directory.Exists(fullPath) ? Directory.GetLastWriteTimeUtc(fullPath) : null
                : info?.Exists == true ? info.LastWriteTimeUtc : null,
            RemoteETag = remoteItem.ETag,
            RemoteLastModifiedUtc = remoteItem.LastModifiedUtc,
            LastConflict = string.Empty
        };
    }

    private static bool LocalChangedSinceState(string fullPath, SyncItemState state)
    {
        if (!File.Exists(fullPath))
        {
            return false;
        }

        var info = new FileInfo(fullPath);
        if (state.LastKnownLocalWriteUtc is null)
        {
            return true;
        }

        if (state.LastKnownLocalSize != info.Length)
        {
            return true;
        }

        var delta = (info.LastWriteTimeUtc - state.LastKnownLocalWriteUtc.Value).Duration();
        return delta > TimeSpan.FromSeconds(2);
    }

    private static async Task WaitForFileReadyAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length >= 0)
                {
                    return;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private void SuppressPath(string relativePath)
    {
        _suppressedUntilUtc[relativePath] = DateTime.UtcNow.Add(_suppressionDuration);
    }

    private bool IsSuppressed(string relativePath)
    {
        if (!_suppressedUntilUtc.TryGetValue(relativePath, out var untilUtc))
        {
            return false;
        }

        if (untilUtc > DateTime.UtcNow)
        {
            return true;
        }

        _suppressedUntilUtc.TryRemove(relativePath, out _);
        return false;
    }

    private void PublishStatus(string message)
    {
        StatusChanged?.Invoke(message);
    }

    private void PublishRemotePollFailure(Exception ex)
    {
        DebugLog.Write($"Remote sync poll failed: {ex}");
        PublishStatus("Remote sync check failed: " + ex.Message);
    }

    private void PublishProcessLocalChangeFailure(string relativePath, Exception ex)
    {
        DebugLog.Write($"ProcessLocalChange failed: {ex}");
        _visualStates[relativePath] = SyncVisualState.Pending;
        PublishStatus($"Sync failed for {relativePath}: {ex.Message}");
    }

    private async Task<R2ObjectInfo> LoadCreatedFolderInfoAsync(AppConfig config, string relativePath, CancellationToken cancellationToken)
    {
        return await _bucketService.HeadObjectAsync(config, relativePath.TrimEnd('/') + "/", cancellationToken)
            ?? new R2ObjectInfo { Key = relativePath.TrimEnd('/') + "/", IsFolder = true };
    }

    private async Task<R2ObjectInfo> LoadUploadedFileInfoAsync(AppConfig config, string relativePath, string fullPath, CancellationToken cancellationToken)
    {
        return await _bucketService.HeadObjectAsync(config, relativePath, cancellationToken)
            ?? new R2ObjectInfo { Key = relativePath, Size = new FileInfo(fullPath).Length, LastModifiedUtc = File.GetLastWriteTimeUtc(fullPath) };
    }

    private static async void ObserveBackgroundTask(Task task, string relativePath)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (AmazonS3Exception ex)
        {
            LogBackgroundFailure(relativePath, ex);
        }
        catch (IOException ex)
        {
            LogBackgroundFailure(relativePath, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogBackgroundFailure(relativePath, ex);
        }
        catch (InvalidOperationException ex)
        {
            LogBackgroundFailure(relativePath, ex);
        }
    }

    private static void LogBackgroundFailure(string relativePath, Exception ex)
    {
        DebugLog.Write($"Background sync task failed: {ex}");
    }

    private SyncVisualState GetAggregateVisualState(string relativePath, bool isFolder)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return SyncVisualState.Synced;
        }

        var prefix = relativePath.TrimEnd('/') + "/";

        var childStates = _visualStates
            .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .ToList();

        if (childStates.Count == 0)
        {
            var hasSyncedChildren = _state.Keys.Any(path => path.StartsWith(prefix, StringComparison.Ordinal));
            return hasSyncedChildren
                ? SyncVisualState.Synced
                : SyncVisualState.Pending;
        }

        if (childStates.Contains(SyncVisualState.Syncing))
        {
            return SyncVisualState.Syncing;
        }

        if (childStates.Contains(SyncVisualState.Pending))
        {
            return SyncVisualState.Pending;
        }

        return SyncVisualState.Synced;
    }

    private static string ToRelativePath(R2ObjectInfo item)
    {
        return item.IsFolder
            ? item.Key.TrimEnd('/')
            : item.Key;
    }

    private static string GetParentPrefix(string relativePath)
    {
        var slashIndex = relativePath.LastIndexOf('/');
        return slashIndex < 0 ? string.Empty : relativePath[..(slashIndex + 1)];
    }

    private static string CombineRelative(string prefix, string name)
    {
        var normalizedPrefix = prefix.Replace('\\', '/').Trim('/');
        var normalizedName = name.Replace('\\', '/').Trim('/');

        return string.IsNullOrEmpty(normalizedPrefix)
            ? normalizedName
            : normalizedPrefix + "/" + normalizedName;
    }

    private static string GetFullPath(string rootPath, string relativePath)
    {
        var segments = relativePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Aggregate(rootPath, Path.Combine);
    }

    private static string GetRelativePath(AppConfig config, string fullPath)
    {
        var rootPath = ExpandHome(config.SyncFolderPath.Trim());
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return string.Empty;
        }

        var relativePath = Path.GetRelativePath(rootPath, fullPath)
            .Replace('\\', '/');

        return relativePath == "." ? string.Empty : relativePath.Trim('/');
    }

    private static string ExpandHome(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("~/", StringComparison.Ordinal))
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, path[2..]);
    }

    private static AppConfig CloneConfig(AppConfig config)
    {
        return new AppConfig
        {
            StorageMode = config.StorageMode,
            EndpointOrAccountId = config.EndpointOrAccountId,
            BucketName = config.BucketName,
            AccessKeyId = config.AccessKeyId,
            SecretAccessKey = config.SecretAccessKey,
            SyncFolderPath = config.SyncFolderPath
        };
    }
}
