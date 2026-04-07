using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;
using DropAndForget.Services.Cloudflare;
using DropAndForget.Services.Config;

namespace DropAndForget.Services.Sync;

public class StorageModeCoordinator(ISyncModeService syncModeService, AppConfigValidator configValidator) : IStorageModeCoordinator
{
    private readonly ISyncModeService _syncModeService = syncModeService ?? throw new ArgumentNullException(nameof(syncModeService));
    private readonly AppConfigValidator _configValidator = configValidator ?? throw new ArgumentNullException(nameof(configValidator));

    public event Action? Changed
    {
        add => _syncModeService.Changed += value;
        remove => _syncModeService.Changed -= value;
    }

    public event Action<string>? StatusChanged
    {
        add => _syncModeService.StatusChanged += value;
        remove => _syncModeService.StatusChanged -= value;
    }

    public async Task ApplyAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.StorageMode == StorageMode.Sync)
        {
            _configValidator.ValidatePersistableConfig(config);
            await R2UserFacingErrors.ExecuteAsync(() => _syncModeService.StartAsync(config, cancellationToken), "Couldn't start sync mode.");
            return;
        }

        await _syncModeService.StopAsync();
    }

    public Task CopyIntoSyncFolderAsync(AppConfig config, IReadOnlyList<string> sourcePaths, string currentPrefix, CancellationToken cancellationToken = default)
    {
        _configValidator.ValidatePersistableConfig(config);
        return R2UserFacingErrors.ExecuteAsync(() => _syncModeService.CopyIntoSyncFolderAsync(config, sourcePaths, currentPrefix, cancellationToken), "Couldn't copy into sync folder.");
    }

    public Task DownloadLocalFolderAsZipAsync(AppConfig config, string relativePath, Stream destination, CancellationToken cancellationToken = default)
    {
        _configValidator.ValidatePersistableConfig(config);
        var folderPath = string.IsNullOrEmpty(relativePath)
            ? _syncModeService.GetSyncFolderPath(config)
            : Path.Combine(_syncModeService.GetSyncFolderPath(config), relativePath.Replace('/', Path.DirectorySeparatorChar));
        return R2UserFacingErrors.ExecuteAsync(() => _syncModeService.DownloadLocalFolderAsZipAsync(folderPath, destination, cancellationToken), "Couldn't download sync folder.");
    }

    public Task ReconcileNowAsync(CancellationToken cancellationToken = default)
    {
        return R2UserFacingErrors.ExecuteAsync(() => _syncModeService.ReconcileNowAsync(cancellationToken), "Couldn't reconcile sync state.");
    }

    public Task StopAsync()
    {
        return _syncModeService.StopAsync();
    }

    public SyncVisualState GetVisualState(string relativePath, bool isFolder = false)
    {
        return _syncModeService.GetVisualState(relativePath, isFolder);
    }
}
