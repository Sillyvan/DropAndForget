using System;
using System.Collections.Generic;

namespace DropAndForget.Models;

public sealed class EncryptedIndex
{
    public int Version { get; set; } = 1;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<EncryptedIndexEntry> Entries { get; set; } = [];
}
