using System;

namespace DropAndForget.Models;

public class R2ObjectInfo
{
    public string Key { get; set; } = string.Empty;

    public bool IsFolder { get; set; }

    public long Size { get; set; }

    public DateTime LastModifiedUtc { get; set; }

    public string ETag { get; set; } = string.Empty;
}
