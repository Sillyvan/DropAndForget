using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;

namespace DropAndForget.Services.Sync;

/// <summary>
/// Coordinates storage mode transitions and sync helpers.
/// </summary>
public interface IStorageModeCoordinator
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
    /// Applies the configured storage mode.
    /// </summary>
    Task ApplyAsync(AppConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies dropped content into the sync folder.
    /// </summary>
    Task CopyIntoSyncFolderAsync(AppConfig config, IReadOnlyList<string> sourcePaths, string currentPrefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a local folder as a zip archive.
    /// </summary>
    Task DownloadLocalFolderAsZipAsync(AppConfig config, string relativePath, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs one remote reconciliation pass.
    /// </summary>
    Task ReconcileNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops active sync work.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Gets the visual sync state for an item.
    /// </summary>
    SyncVisualState GetVisualState(string relativePath, bool isFolder = false);
}
