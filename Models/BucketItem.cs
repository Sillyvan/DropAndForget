using System;
using System.IO;

namespace DropAndForget.Models;

public class BucketItem
{
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string FolderPath { get; set; } = string.Empty;

    public string FolderPathDisplay => string.IsNullOrEmpty(FolderPath) ? "/" : FolderPath;

    public long? SizeBytes { get; set; }

    public string SizeText { get; set; } = string.Empty;

    public DateTime? ModifiedAt { get; set; }

    public string ModifiedText { get; set; } = string.Empty;

    public bool IsFolder { get; set; }

    public bool IsFile => !IsFolder;

    public string KindText => IsFolder ? "Folder" : "File";

    public bool IsImageFile => MatchesExtension(".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".ico");

    public bool IsPdfFile => MatchesExtension(".pdf");

    public bool IsVideoFile => MatchesExtension(".mp4", ".mov", ".avi", ".mkv", ".webm", ".m4v");

    public bool IsAudioFile => MatchesExtension(".mp3", ".wav", ".m4a", ".ogg", ".flac");

    public bool IsArchiveFile => MatchesExtension(".zip", ".rar", ".7z", ".tar", ".gz");

    public bool IsSpreadsheetFile => MatchesExtension(".csv", ".xls", ".xlsx");

    public bool IsCodeFile => MatchesExtension(".cs", ".js", ".ts", ".tsx", ".jsx", ".json", ".xml", ".yml", ".yaml", ".html", ".htm", ".css", ".sql", ".sh");

    public bool IsTextFile => MatchesExtension(".txt", ".md", ".log");

    public bool IsGenericFile => IsFile
        && !IsImageFile
        && !IsPdfFile
        && !IsVideoFile
        && !IsAudioFile
        && !IsArchiveFile
        && !IsSpreadsheetFile
        && !IsCodeFile
        && !IsTextFile;

    private bool MatchesExtension(params string[] extensions)
    {
        if (IsFolder)
        {
            return false;
        }

        var extension = Path.GetExtension(Key);
        return extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
