using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DropAndForget.UI;
using DropAndForget.Models;
using DropAndForget.Services.Diagnostics;
using DropAndForget.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using AppUi = DropAndForget.UI;

namespace DropAndForget;

public partial class MainWindow : AppUi.Window
{
    private const string BucketItemsDragPrefix = "daf:items:";
    private static readonly IBrush DropRowBrush = new SolidColorBrush(Color.Parse("#263B82F6"));
    private static readonly Cursor DragMoveCursor = new(StandardCursorType.SizeAll);
    private DataGridRow? _activeDropRow;
    private Button? _activeBreadcrumbButton;
    private ContextMenu? _activeContextMenu;
    private IReadOnlyList<BucketListEntry> _contextMenuItems = [];
    private BucketListEntry? _pendingDragItem;
    private IReadOnlyList<BucketListEntry> _pendingDragItems = [];
    private Point _pendingDragStart;
    private PointerPressedEventArgs? _pendingDragTrigger;
    private bool _isInternalDragActive;

    public MainWindow()
        : this(Program.Services.GetRequiredService<MainWindowViewModel>())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.DialogManager.Register<PreviewDialogContent, PreviewDialogViewModel>();

        var bucketView = this.FindControl<Grid>("BucketView");
        if (bucketView is not null)
        {
            DragDrop.AddDragEnterHandler(bucketView, OnDragEnter);
            DragDrop.AddDragLeaveHandler(bucketView, OnDragLeave);
            DragDrop.AddDragOverHandler(bucketView, OnDragOver);
            DragDrop.AddDropHandler(bucketView, OnDrop);
        }

        var bucketItemsGrid = this.FindControl<DataGrid>("BucketItemsGrid");
        if (bucketItemsGrid is not null)
        {
            bucketItemsGrid.AddHandler(InputElement.PointerPressedEvent, BucketRow_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            bucketItemsGrid.AddHandler(InputElement.PointerMovedEvent, BucketRow_PointerMoved, handledEventsToo: true);
            bucketItemsGrid.AddHandler(InputElement.PointerReleasedEvent, BucketRow_PointerReleased, handledEventsToo: true);
            bucketItemsGrid.AddHandler(InputElement.PointerCaptureLostEvent, BucketRow_PointerCaptureLost, handledEventsToo: true);
        }

        AddHandler(InputElement.PointerPressedEvent, MainWindow_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);

        Closed += OnClosed;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private BucketBrowserViewModel? Bucket => ViewModel?.Bucket;

    private ConnectionSetupViewModel? Setup => ViewModel?.Setup;

    private async void OnClosed(object? sender, System.EventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.StopSyncAsync();
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (IsInternalBucketDrag(e))
        {
            return;
        }

        if (ViewModel is not null)
        {
            ViewModel.IsDropTargetActive = HasFiles(e);
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (IsInternalBucketDrag(e))
        {
            return;
        }

        if (ViewModel is not null)
        {
            ViewModel.IsDropTargetActive = false;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (IsInternalBucketDrag(e))
        {
            ViewModel!.IsDropTargetActive = false;
            return;
        }

        var hasFiles = HasFiles(e);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;

        if (ViewModel is not null)
        {
            ViewModel.IsDropTargetActive = hasFiles;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (ViewModel is null || Bucket is null)
            {
                return;
            }

            if (IsInternalBucketDrag(e))
            {
                return;
            }

            ViewModel.IsDropTargetActive = false;
            var paths = DropPathExtractor.Extract(e.DataTransfer);
            if (paths.Count == 0)
            {
                Bucket.SetStatus("Drop files or folders here.");
                return;
            }

            await Bucket.HandleDroppedFilesAsync(paths);
        }
        catch (System.Exception ex)
        {
            DebugLog.Write($"Unexpected drop upload error: {ex}");
            Bucket?.SetStatus("Unexpected upload error.");
        }
    }

    private async void UploadFilesButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Bucket is null)
            {
                return;
            }

            var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (OperatingSystem.IsMacOS())
            {
                var macPaths = await PickFilesWithMacOsScriptAsync();
                if (macPaths.Count == 0)
                {
                    Bucket.SetStatus("Upload cancelled.");
                    return;
                }

                await Bucket.HandleDroppedFilesAsync(macPaths);
                return;
            }

            if (storageProvider is null)
            {
                Bucket.SetStatus("File picker unavailable.");
                return;
            }

            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Upload files",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    FilePickerFileTypes.All
                ]
            });

            var paths = files
                .Select(file => file.Path.LocalPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct()
                .ToList();

            if (paths.Count == 0)
            {
                Bucket.SetStatus("Upload cancelled.");
                return;
            }

            await Bucket.HandleDroppedFilesAsync(paths);
        }
        catch (System.Exception ex)
        {
            DebugLog.Write($"Unexpected picker upload error: {ex}");
            Bucket?.SetStatus("Unexpected upload error.");
        }
    }

    private static async Task<IReadOnlyList<string>> PickFilesWithMacOsScriptAsync()
    {
        const string script = "set pickedFiles to choose file with prompt \"Upload files\" with multiple selections allowed\nset outputLines to {}\nrepeat with pickedFile in pickedFiles\nset end of outputLines to POSIX path of pickedFile\nend repeat\nset AppleScript's text item delimiters to linefeed\nreturn outputLines as text";

        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(script);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Couldn't start macOS file picker.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            if (error.Contains("User canceled", System.StringComparison.OrdinalIgnoreCase)
                || error.Contains("-128", System.StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "macOS file picker failed."
                : $"macOS file picker failed: {error.Trim()}");
        }

        return output
            .Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();
    }

    private async void PickSyncFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        if (Setup is null)
        {
            return;
        }

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            Setup.SetStatus("Folder picker unavailable.");
            return;
        }

        var suggestedPath = string.IsNullOrWhiteSpace(Setup.SyncFolderPath)
            ? null
            : await storageProvider.TryGetFolderFromPathAsync(Setup.SyncFolderPath);

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose sync folder",
            SuggestedStartLocation = suggestedPath,
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder is null)
        {
            return;
        }

        Setup.SetSyncFolderPath(folder.Path.LocalPath);
        Setup.SetStatus("Sync folder picked.");
    }

    private async void BucketItemsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Bucket is null)
        {
            return;
        }

        var item = Bucket.SelectedBucketItem;
        if (item?.IsFolder == true)
        {
            await Bucket.OpenFolderAsync(item);
            return;
        }

        await PreviewBucketItemAsync(Bucket, item);
    }

    private void BucketItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed
            || Bucket is null
            || sender is not Control { DataContext: BucketListEntry item })
        {
            return;
        }

        UpdateSelectionForContextClick(item);
    }

    private void BucketItemsList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed
            || Bucket is null
            || e.Source is not StyledElement element)
        {
            return;
        }

        for (var current = element; current is not null; current = current.Parent)
        {
            if (current is DataGridRow { DataContext: BucketListEntry item })
            {
                UpdateSelectionForContextClick(item);
                DebugLog.Write($"Context target row: {item.Key}");
                return;
            }
        }

        ClearGridSelection();
        DebugLog.Write("Context target empty area");
    }

    private void BucketItemsGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (BucketItemsGrid?.SelectedItems is null || Bucket is null)
        {
            return;
        }

        Bucket.SetSelectedBucketItems(BucketItemsGrid.SelectedItems.Cast<BucketListEntry>());
    }

    private void BucketItemsGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.SetValue(DragDrop.AllowDropProperty, true);
        DragDrop.AddDragEnterHandler(e.Row, BucketRow_DragEnter);
        DragDrop.AddDragLeaveHandler(e.Row, BucketRow_DragLeave);
        DragDrop.AddDragOverHandler(e.Row, BucketRow_DragOver);
        DragDrop.AddDropHandler(e.Row, BucketRow_Drop);
    }

    private void BucketItemsGrid_UnloadingRow(object? sender, DataGridRowEventArgs e)
    {
        DragDrop.RemoveDragEnterHandler(e.Row, BucketRow_DragEnter);
        DragDrop.RemoveDragLeaveHandler(e.Row, BucketRow_DragLeave);
        DragDrop.RemoveDragOverHandler(e.Row, BucketRow_DragOver);
        DragDrop.RemoveDropHandler(e.Row, BucketRow_Drop);
        ClearDropRowHighlight(e.Row);
    }

    private void NewFolderMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (Bucket is null || !Bucket.CanCreateFolderHere())
        {
            return;
        }

        Bucket.BeginNewFolder();
        if (Bucket.SelectedBucketItem is { } item)
        {
            FocusRenameTextBox(item);
        }
    }

    private void RenameBucketItemMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var item = _contextMenuItems.Count == 1 ? _contextMenuItems[0] : null;
        if (Bucket is null || item is null || !Bucket.CanRenameItem(item))
        {
            return;
        }

        Bucket.BeginRename(item);
        FocusRenameTextBox(item);
    }

    private void DeleteBucketItemMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (Bucket is null || _contextMenuItems.Count == 0)
        {
            return;
        }

        if (_contextMenuItems.Count == 1)
        {
            Bucket.DeleteBucketItemCommand.Execute(_contextMenuItems[0]);
            return;
        }

        MainWindowUiSupport.ObserveBackgroundTask(Bucket.DeleteItemsAsync(_contextMenuItems), "delete items");
    }

    private async void DownloadBucketItemMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (Bucket is null || _contextMenuItems.Count == 0)
        {
            return;
        }

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            Bucket.SetStatus("Save dialog unavailable.");
            return;
        }

        if (_contextMenuItems.Count > 1)
        {
            var zipFile = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Download selection as zip",
                SuggestedFileName = "selection.zip"
            });

            if (zipFile is null)
            {
                Bucket.SetStatus("Zip download cancelled.");
                return;
            }

            await using var zipStream = await zipFile.OpenWriteAsync();
            if (zipStream.CanSeek)
            {
                zipStream.SetLength(0);
            }

            await Bucket.DownloadItemsAsZipAsync(_contextMenuItems, zipStream);
            return;
        }

        var item = _contextMenuItems[0];
        if (!Bucket.CanDownloadItem(item))
        {
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Download file",
            SuggestedFileName = item.DisplayName
        });

        if (file is null)
        {
            Bucket.SetStatus("Download cancelled.");
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek)
        {
            stream.SetLength(0);
        }

        await Bucket.DownloadItemAsync(item, stream);
    }

    private async void PreviewBucketItemMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var item = _contextMenuItems.Count == 1 ? _contextMenuItems[0] : null;
        if (Bucket is null || item is null)
        {
            return;
        }

        await PreviewBucketItemAsync(Bucket, item);
    }

    private async void DownloadFolderAsZipMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var item = _contextMenuItems.Count == 1 ? _contextMenuItems[0] : null;
        if (Bucket is null || item is null || !Bucket.CanDownloadFolderAsZip(item))
        {
            return;
        }

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            Bucket.SetStatus("Save dialog unavailable.");
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Download folder as zip",
            SuggestedFileName = item.DisplayName.TrimEnd('/') + ".zip"
        });

        if (file is null)
        {
            Bucket.SetStatus("Zip download cancelled.");
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek)
        {
            stream.SetLength(0);
        }

        await Bucket.DownloadFolderAsZipAsync(item, stream);
    }

    private void BucketItemsContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu contextMenu || Bucket is null)
        {
            return;
        }

        _activeContextMenu = contextMenu;

        var item = Bucket.SelectedBucketItem;
        _contextMenuItems = Bucket.GetEffectiveSelectedBucketItems(item);
        contextMenu.DataContext = item;
        DebugLog.Write(_contextMenuItems.Count == 0 ? "Opening context menu for empty area" : $"Opening context menu for {_contextMenuItems.Count} item(s)");

        foreach (var control in contextMenu.Items.OfType<MenuItem>())
        {
            switch (control.Header as string)
            {
                case "New folder":
                    control.IsVisible = _contextMenuItems.Count == 0;
                    control.IsEnabled = _contextMenuItems.Count == 0 && Bucket.CanCreateFolderHere();
                    break;
                case "Preview":
                    control.IsVisible = _contextMenuItems.Count == 1 && item?.CanPreview == true;
                    break;
                case "Download":
                    control.IsVisible = (_contextMenuItems.Count == 1 && item?.IsFile == true)
                        || Bucket.CanDownloadSelectionAsZip(_contextMenuItems);
                    break;
                case "Download as zip":
                    control.IsVisible = _contextMenuItems.Count == 1 && item?.IsFolder == true;
                    break;
                case "Rename":
                    control.IsVisible = _contextMenuItems.Count == 1;
                    control.IsEnabled = _contextMenuItems.Count == 1 && item is not null && Bucket.CanRenameItem(item);
                    break;
                case "Delete":
                    control.IsVisible = _contextMenuItems.Count > 0;
                    control.IsEnabled = _contextMenuItems.Count > 0;
                    break;
            }
        }
    }

    private void BucketItemsContextMenu_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (ReferenceEquals(_activeContextMenu, sender))
        {
            _activeContextMenu = null;
        }
    }

    private void MainWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var contextMenu = _activeContextMenu;
        if (contextMenu is null
            || !contextMenu.IsOpen
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || IsSourceWithinContextMenu(e.Source, contextMenu))
        {
            return;
        }

        contextMenu.Close();
    }

    private void RenameTextBox_AttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not BucketListEntry { IsEditing: true })
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private async void RenameTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not BucketListEntry item || Bucket is null)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await TryCommitRenameAsync(Bucket, item, textBox);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Bucket.CancelRename(item);
        }
    }

    private void UnlockPassphraseTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Bucket?.UnlockEncryptedBucketCommand is not { } command)
        {
            return;
        }

        if (!command.CanExecute(null))
        {
            return;
        }

        e.Handled = true;
        command.Execute(null);
    }

    private async void RenameTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not BucketListEntry { IsEditing: true } item || Bucket is null)
        {
            return;
        }

        if (item.IsCommittingEdit)
        {
            return;
        }

        if (item.IsNewPlaceholder)
        {
            await TryCommitRenameAsync(Bucket, item, textBox);
            return;
        }

        Bucket.CancelRename(item);
    }

    private void BucketItemsList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || Bucket is null)
        {
            return;
        }

        var item = Bucket.SelectedBucketItem;
        if (item is null || !Bucket.CanRenameItem(item))
        {
            return;
        }

        e.Handled = true;
        Bucket.BeginRename(item);
        FocusRenameTextBox(item);
    }

    private void FocusRenameTextBox(BucketListEntry item)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var control in this.GetVisualDescendants())
            {
                if (control is TextBox textBox && ReferenceEquals(textBox.DataContext, item) && textBox.IsVisible)
                {
                    textBox.Focus();
                    textBox.SelectAll();
                    break;
                }
            }
        }, DispatcherPriority.Input);
    }

    private static bool HasFiles(DragEventArgs e)
    {
        return DropPathExtractor.Extract(e.DataTransfer).Count > 0;
    }

    private void BucketRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var row = GetBucketRowFromEventSource(e.Source);
        var currentPoint = e.GetCurrentPoint(this);
        if (!currentPoint.Properties.IsLeftButtonPressed
            || Bucket is null
            || row?.DataContext is not BucketListEntry item)
        {
            ClearPendingDrag();
            return;
        }

        var selectedItems = Bucket.GetEffectiveSelectedBucketItems(item);
        var preserveSelectionForDrag = Bucket.SelectedBucketItemCount > 1
            && Bucket.IsItemSelected(item)
            && !HasSelectionModifier(e.KeyModifiers);

        if (preserveSelectionForDrag)
        {
            e.Handled = true;
        }

        if (!Bucket.CanStartDrag(item))
        {
            ClearPendingDrag();
            return;
        }

        _pendingDragItems = preserveSelectionForDrag
            ? selectedItems
            : Bucket.IsItemSelected(item)
                ? selectedItems
            : [item];
        if (!Bucket.CanStartDrag(_pendingDragItems))
        {
            ClearPendingDrag();
            return;
        }

        _pendingDragItem = item;
        _pendingDragStart = e.GetPosition(this);
        _pendingDragTrigger = e;
        e.Pointer.Capture(sender as IInputElement);
    }

    private async void BucketRow_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isInternalDragActive
            || Bucket is null
            || _pendingDragItem is null
            || _pendingDragTrigger is null
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (System.Math.Abs(position.X - _pendingDragStart.X) < 6 && System.Math.Abs(position.Y - _pendingDragStart.Y) < 6)
        {
            return;
        }

        var dragItem = _pendingDragItem;
        var dragItems = _pendingDragItems;
        var dragTrigger = _pendingDragTrigger;
        ClearPendingDrag();
        if (!Bucket.CanStartDrag(dragItems))
        {
            return;
        }

        var dataItem = new DataTransferItem();
        dataItem.SetText(BucketItemsDragPrefix + string.Join("\n", dragItems.Select(static item => item.Key)));
        var data = new DataTransfer();
        data.Add(dataItem);

        _isInternalDragActive = true;
        Cursor = DragMoveCursor;
        try
        {
            await DragDrop.DoDragDropAsync(dragTrigger, data, DragDropEffects.Move);
        }
        finally
        {
            dragTrigger.Pointer.Capture(null);
            _isInternalDragActive = false;
            Cursor = Cursor.Default;
            ClearBreadcrumbDropHighlight();
            ClearDropRowHighlight();
        }
    }

    private void BucketRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        ClearPendingDrag();
    }

    private void BucketRow_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ClearPendingDrag();
    }

    private void BucketRow_DragEnter(object? sender, DragEventArgs e)
    {
        UpdateRowDragState(sender as DataGridRow, e);
    }

    private void BreadcrumbButton_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        DragDrop.AddDragEnterHandler(button, BreadcrumbButton_DragEnter);
        DragDrop.AddDragLeaveHandler(button, BreadcrumbButton_DragLeave);
        DragDrop.AddDragOverHandler(button, BreadcrumbButton_DragOver);
        DragDrop.AddDropHandler(button, BreadcrumbButton_Drop);
    }

    private void BreadcrumbButton_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        DragDrop.RemoveDragEnterHandler(button, BreadcrumbButton_DragEnter);
        DragDrop.RemoveDragLeaveHandler(button, BreadcrumbButton_DragLeave);
        DragDrop.RemoveDragOverHandler(button, BreadcrumbButton_DragOver);
        DragDrop.RemoveDropHandler(button, BreadcrumbButton_Drop);
    }

    private void BreadcrumbButton_DragEnter(object? sender, DragEventArgs e)
    {
        UpdateBreadcrumbDragState(sender as Button, e);
    }

    private void BreadcrumbButton_DragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Button button)
        {
            ClearBreadcrumbDropHighlight(button);
        }
    }

    private void BreadcrumbButton_DragOver(object? sender, DragEventArgs e)
    {
        UpdateBreadcrumbDragState(sender as Button, e);
    }

    private async void BreadcrumbButton_Drop(object? sender, DragEventArgs e)
    {
        if (Bucket is null
            || sender is not Button { DataContext: BreadcrumbItem breadcrumb }
            || !TryResolveDraggedItems(e, out var items)
            || !Bucket.CanMoveItemsToPath(items, breadcrumb.Prefix))
        {
            return;
        }

        e.Handled = true;
        e.DragEffects = DragDropEffects.Move;
        ClearDropRowHighlight();
        await Bucket.MoveItemsAsync(items, breadcrumb.Prefix, breadcrumb.Label);
    }

    private void BucketRow_DragLeave(object? sender, DragEventArgs e)
    {
        if (sender is DataGridRow row)
        {
            ClearDropRowHighlight(row);
        }
    }

    private void BucketRow_DragOver(object? sender, DragEventArgs e)
    {
        UpdateRowDragState(sender as DataGridRow, e);
    }

    private async void BucketRow_Drop(object? sender, DragEventArgs e)
    {
        if (Bucket is null
            || sender is not DataGridRow { DataContext: BucketListEntry targetFolder }
            || !TryResolveDraggedItems(e, out var items)
            || !Bucket.CanMoveItems(items, targetFolder))
        {
            return;
        }

        e.Handled = true;
        e.DragEffects = DragDropEffects.Move;
        ClearDropRowHighlight();
        await Bucket.MoveItemsAsync(items, targetFolder);
    }

    private void UpdateRowDragState(DataGridRow? row, DragEventArgs e)
    {
        if (Bucket is null || row?.DataContext is not BucketListEntry targetFolder)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        if (!TryResolveDraggedItems(e, out var items) || !Bucket.CanMoveItems(items, targetFolder))
        {
            e.DragEffects = DragDropEffects.None;
            ClearDropRowHighlight(row);
            return;
        }

        e.Handled = true;
        e.DragEffects = DragDropEffects.Move;
        SetDropRowHighlight(row);
    }

    private void UpdateBreadcrumbDragState(Button? button, DragEventArgs e)
    {
        ClearDropRowHighlight();
        if (Bucket is null || button?.DataContext is not BreadcrumbItem breadcrumb)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        if (!TryResolveDraggedItems(e, out var items) || !Bucket.CanMoveItemsToPath(items, breadcrumb.Prefix))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.Handled = true;
        e.DragEffects = DragDropEffects.Move;
        SetBreadcrumbDropHighlight(button);
    }

    private bool TryResolveDraggedItems(DragEventArgs e, out IReadOnlyList<BucketListEntry> items)
    {
        items = [];
        if (Bucket is null)
        {
            return false;
        }

        var text = e.DataTransfer.TryGetText();
        if (text is null || !text.StartsWith(BucketItemsDragPrefix, System.StringComparison.Ordinal))
        {
            return false;
        }

        items = text[BucketItemsDragPrefix.Length..]
            .Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(Bucket.FindVisibleItem)
            .Where(static item => item is not null)
            .Cast<BucketListEntry>()
            .ToList();
        return items.Count > 0;
    }

    private void SetDropRowHighlight(DataGridRow row)
    {
        if (ReferenceEquals(_activeDropRow, row))
        {
            return;
        }

        ClearDropRowHighlight();
        _activeDropRow = row;
        row.Background = DropRowBrush;
    }

    private void ClearDropRowHighlight(DataGridRow? row = null)
    {
        var targetRow = row ?? _activeDropRow;
        if (targetRow is null)
        {
            return;
        }

        targetRow.Background = null;
        if (ReferenceEquals(_activeDropRow, targetRow))
        {
            _activeDropRow = null;
        }
    }

    private void ClearPendingDrag()
    {
        _pendingDragTrigger = null;
        _pendingDragItem = null;
        _pendingDragItems = [];
        _pendingDragStart = default;
    }

    private static bool IsInternalBucketDrag(DragEventArgs e)
    {
        var text = e.DataTransfer.TryGetText();
        return text is not null && text.StartsWith(BucketItemsDragPrefix, System.StringComparison.Ordinal);
    }

    private static DataGridRow? GetBucketRowFromEventSource(object? source)
    {
        return (source as Visual)?.FindAncestorOfType<DataGridRow>();
    }

    private static bool IsSourceWithinContextMenu(object? source, ContextMenu contextMenu)
    {
        for (var current = source as StyledElement; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, contextMenu))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSelectionModifier(KeyModifiers modifiers)
    {
        return modifiers.HasFlag(KeyModifiers.Shift)
            || modifiers.HasFlag(KeyModifiers.Control)
            || modifiers.HasFlag(KeyModifiers.Meta)
            || modifiers.HasFlag(KeyModifiers.Alt);
    }

    private void SetBreadcrumbDropHighlight(Button button)
    {
        if (ReferenceEquals(_activeBreadcrumbButton, button))
        {
            return;
        }

        ClearBreadcrumbDropHighlight();
        _activeBreadcrumbButton = button;
        button.Classes.Add("breadcrumb-drop-target");
    }

    private void ClearBreadcrumbDropHighlight(Button? button = null)
    {
        var targetButton = button ?? _activeBreadcrumbButton;
        if (targetButton is null)
        {
            return;
        }

        targetButton.Classes.Remove("breadcrumb-drop-target");
        if (ReferenceEquals(_activeBreadcrumbButton, targetButton))
        {
            _activeBreadcrumbButton = null;
        }
    }

    private async Task PreviewBucketItemAsync(BucketBrowserViewModel viewModel, BucketListEntry? item)
    {
        if (item is null || !viewModel.CanPreviewItem(item))
        {
            return;
        }

        var preview = await viewModel.LoadPreviewAsync(item);
        if (preview is null)
        {
            return;
        }

        switch (preview.Kind)
        {
            case FilePreviewKind.Image:
            case FilePreviewKind.Text:
            case FilePreviewKind.Pdf:
                break;
            default:
                return;
        }

        viewModel.DialogManager
            .CreateDialog(new PreviewDialogViewModel(viewModel.DialogManager, preview))
            .WithMaxWidth(1040)
            .Dismissible()
            .Show();
    }

    private static async Task TryCommitRenameAsync(BucketBrowserViewModel viewModel, BucketListEntry item, TextBox textBox)
    {
        item.IsCommittingEdit = true;

        try
        {
            var renamed = await viewModel.RenameItemAsync(item, item.EditName);
            if (!renamed && item.IsEditing)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }
        finally
        {
            item.IsCommittingEdit = false;
        }
    }

    private void UpdateSelectionForContextClick(BucketListEntry item)
    {
        if (Bucket is null || BucketItemsGrid?.SelectedItems is null)
        {
            return;
        }

        if (Bucket.IsItemSelected(item))
        {
            return;
        }

        BucketItemsGrid.SelectedItems.Clear();
        BucketItemsGrid.SelectedItems.Add(item);
        BucketItemsGrid.SelectedItem = item;
    }

    private void ClearGridSelection()
    {
        if (BucketItemsGrid?.SelectedItems is not null)
        {
            BucketItemsGrid.SelectedItems.Clear();
        }

        if (Bucket is not null)
        {
            Bucket.SelectedBucketItem = null;
            Bucket.SetSelectedBucketItems([]);
        }
    }
}
