using DropAndForget.ViewModels;

namespace DropAndForget.Models;

public class UploadItem : ViewModelBase
{
    private UploadState _state;
    private string _message = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string ObjectKey { get; set; } = string.Empty;

    public UploadState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }
}
