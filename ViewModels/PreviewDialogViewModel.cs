using DropAndForget.Models;
using DropAndForget.UI;

namespace DropAndForget.ViewModels;

public sealed class PreviewDialogViewModel
{
    public PreviewDialogViewModel(DialogManager dialogManager, FilePreviewData preview)
    {
        DialogManager = dialogManager;
        Preview = preview;
    }

    public DialogManager DialogManager { get; }

    public FilePreviewData Preview { get; }

    public string Title => Preview.Title;

    public bool IsPdf => Preview.Kind == FilePreviewKind.Pdf;
}
