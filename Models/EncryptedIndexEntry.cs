using System;

namespace DropAndForget.Models;

public sealed class EncryptedIndexEntry
{
    public string RelativePath { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsFolder { get; set; }

    public string ObjectId { get; set; } = string.Empty;

    public string WrappedFileKey { get; set; } = string.Empty;

    public string CipherAlgorithm { get; set; } = "XChaCha20-Poly1305";

    public long Size { get; set; }

    public string? ContentType { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ModifiedAtUtc { get; set; }
}
