using System.ComponentModel;
using System.Runtime.CompilerServices;
using DropAndForget.Models;

namespace DropAndForget.ViewModels;

public sealed class BucketListEntry : INotifyPropertyChanged
{
    private string _editName;
    private bool _isEditing;
    private bool _isNewPlaceholder;
    private bool _canPreview;
    private bool _isCommittingEdit;
    private string _syncStatusText = "Synced";
    private bool _isSyncSynced = true;
    private bool _isSyncPending;
    private bool _isSyncing;

    public BucketListEntry(BucketItem item)
    {
        Item = item;
        _editName = item.DisplayName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BucketItem Item { get; }

    public string Key => Item.Key;

    public string DisplayName
    {
        get => Item.DisplayName;
        set
        {
            if (Item.DisplayName == value)
            {
                return;
            }

            Item.DisplayName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    public string Detail => Item.Detail;

    public string FolderPath => Item.FolderPath;

    public string FolderPathDisplay => Item.FolderPathDisplay;

    public long? SizeBytes => Item.SizeBytes;

    public string SizeText => Item.SizeText;

    public System.DateTime? ModifiedAt => Item.ModifiedAt;

    public string ModifiedText => Item.ModifiedText;

    public bool IsFolder => Item.IsFolder;

    public bool IsFile => Item.IsFile;

    public string KindText => Item.KindText;

    public bool IsImageFile => Item.IsImageFile;

    public bool IsPdfFile => Item.IsPdfFile;

    public bool IsVideoFile => Item.IsVideoFile;

    public bool IsAudioFile => Item.IsAudioFile;

    public bool IsArchiveFile => Item.IsArchiveFile;

    public bool IsSpreadsheetFile => Item.IsSpreadsheetFile;

    public bool IsCodeFile => Item.IsCodeFile;

    public bool IsTextFile => Item.IsTextFile;

    public bool IsGenericFile => Item.IsGenericFile;

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    public bool IsNewPlaceholder
    {
        get => _isNewPlaceholder;
        set => SetProperty(ref _isNewPlaceholder, value);
    }

    public bool CanPreview
    {
        get => _canPreview;
        set => SetProperty(ref _canPreview, value);
    }

    public bool IsCommittingEdit
    {
        get => _isCommittingEdit;
        set => SetProperty(ref _isCommittingEdit, value);
    }

    public string SyncStatusText
    {
        get => _syncStatusText;
        set => SetProperty(ref _syncStatusText, value);
    }

    public bool IsSyncSynced
    {
        get => _isSyncSynced;
        set => SetProperty(ref _isSyncSynced, value);
    }

    public bool IsSyncPending
    {
        get => _isSyncPending;
        set => SetProperty(ref _isSyncPending, value);
    }

    public bool IsSyncing
    {
        get => _isSyncing;
        set => SetProperty(ref _isSyncing, value);
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
