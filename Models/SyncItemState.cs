using System;

namespace DropAndForget.Models;

public class SyncItemState
{
    public string RelativePath { get; set; } = string.Empty;

    public bool IsFolder { get; set; }

    public long? LastKnownLocalSize { get; set; }

    public DateTime? LastKnownLocalWriteUtc { get; set; }

    public string RemoteETag { get; set; } = string.Empty;

    public DateTime? RemoteLastModifiedUtc { get; set; }

    public string LastConflict { get; set; } = string.Empty;
}
