using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DropAndForget.Models;
using DropAndForget.Serialization;
using DropAndForget.Services.Diagnostics;

namespace DropAndForget.Services.Sync;

public class SyncStateStore
{
    private readonly string _stateRoot;

    public SyncStateStore(string? stateRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(stateRoot))
        {
            _stateRoot = stateRoot;
            return;
        }

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _stateRoot = Path.Combine(appDataPath, "DropAndForget", "sync-state");
    }

    /// <summary>
    /// Loads persisted sync state.
    /// </summary>
    public IReadOnlyDictionary<string, SyncItemState> Load(AppConfig config)
    {
        var path = GetStatePath(config);
        if (!File.Exists(path))
        {
            return new Dictionary<string, SyncItemState>(StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(path);
            var items = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListSyncItemState) ?? [];
            return items
                .Where(item => !string.IsNullOrWhiteSpace(item.RelativePath))
                .ToDictionary(item => item.RelativePath, item => item, StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            DebugLog.Write($"Sync state load failed: invalid json: {ex.Message}");
            return new Dictionary<string, SyncItemState>(StringComparer.Ordinal);
        }
        catch (IOException ex)
        {
            DebugLog.Write($"Sync state load failed: io error: {ex.Message}");
            return new Dictionary<string, SyncItemState>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Saves persisted sync state.
    /// </summary>
    public void Save(AppConfig config, IReadOnlyDictionary<string, SyncItemState> state)
    {
        Directory.CreateDirectory(_stateRoot);
        var path = GetStatePath(config);
        var items = state.Values
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ToList();
        var json = JsonSerializer.Serialize(items, AppJsonSerializerContext.Default.ListSyncItemState);
        File.WriteAllText(path, json);
    }

    private string GetStatePath(AppConfig config)
    {
        var identity = $"{config.EndpointOrAccountId.Trim()}|{config.BucketName.Trim()}|{config.SyncFolderPath.Trim()}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return Path.Combine(_stateRoot, hash + ".json");
    }
}
