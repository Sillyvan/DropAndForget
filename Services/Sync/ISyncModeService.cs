using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;

namespace DropAndForget.Services.Sync;

/// <summary>
/// Watches a local folder and keeps it in sync with R2.
/// </summary>
public interface ISyncModeService
{
    /// <summary>
    /// Raised when visible sync state changes.
    /// </summary>
    event Action? Changed;

    /// <summary>
    /// Raised when sync status text changes.
    /// </summary>
    event Action<string>? StatusChanged;

    /// <summary>
    /// Gets whether sync is running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the active sync folder path.
    /// </summary>
    string CurrentFolderPath { get; }

    /// <summary>
    /// Gets the visual sync state for an item.
    /// </summary>
    SyncVisualState GetVisualState(string relativePath, bool isFolder = false);

    /// <summary>
    /// Gets the resolved sync folder path for config.
    /// </summary>
    string GetSyncFolderPath(AppConfig config);

    /// <summary>
    /// Starts sync mode.
    /// </summary>
    Task StartAsync(AppConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops sync mode.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Runs one remote reconciliation pass.
    /// </summary>
    Task ReconcileNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies dropped content into the sync folder.
    /// </summary>
    Task CopyIntoSyncFolderAsync(AppConfig config, IReadOnlyList<string> sourcePaths, string currentPrefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a local folder as a zip archive.
    /// </summary>
    Task DownloadLocalFolderAsZipAsync(string folderPath, Stream destination, CancellationToken cancellationToken = default);
}
