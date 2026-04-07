using System;

namespace DropAndForget.Models;

public sealed class EncryptedBucketManifest
{
    public int Version { get; set; } = 1;

    public string Kdf { get; set; } = "Argon2id";

    public long Argon2MemorySize { get; set; }

    public long Argon2Iterations { get; set; }

    public int Argon2Parallelism { get; set; } = 1;

    public string Salt { get; set; } = string.Empty;

    public string WrappedBucketMasterKey { get; set; } = string.Empty;

    public string VerificationBlob { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
