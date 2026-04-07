using System.Collections.Generic;
using System.Text.Json.Serialization;
using DropAndForget.Models;
using DropAndForget.Services.Config;
using DropAndForget.Services.Encryption;

namespace DropAndForget.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(PersistedAppConfig))]
[JsonSerializable(typeof(List<SyncItemState>))]
[JsonSerializable(typeof(EncryptedBucketManifest))]
[JsonSerializable(typeof(EncryptedIndex))]
[JsonSerializable(typeof(EncryptedPayloadEnvelope))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
