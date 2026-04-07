using System;
using System.IO;
using System.Text.Json;
using DropAndForget.Models;
using DropAndForget.Serialization;
using DropAndForget.Services.Diagnostics;

namespace DropAndForget.Services.Config;

public class JsonSettingsStore
{
    private readonly string _configPath;
    private readonly LocalSecretProtector _secretProtector;

    public JsonSettingsStore(LocalSecretProtector? secretProtector = null, string? configPath = null)
    {
        _secretProtector = secretProtector ?? new LocalSecretProtector();

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            _configPath = configPath;
            return;
        }

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDirectory = Path.Combine(appDataPath, "DropAndForget");
        _configPath = Path.Combine(appDirectory, "config.json");
    }

    /// <inheritdoc/>
    public SavedConfigLoadResult Load()
    {
        if (!File.Exists(_configPath))
        {
            return new SavedConfigLoadResult(new AppConfig(), null);
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var persisted = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.PersistedAppConfig);
            return new SavedConfigLoadResult(
                persisted?.ToAppConfig(_secretProtector) ?? new AppConfig(),
                persisted is null ? "Saved config was empty. Re-enter connection if needed." : null);
        }
        catch (JsonException ex)
        {
            DebugLog.Write($"Settings load failed: invalid json: {ex.Message}");
            return new SavedConfigLoadResult(new AppConfig(), "Saved config is corrupted. Re-enter connection and save again.");
        }
        catch (IOException ex)
        {
            DebugLog.Write($"Settings load failed: io error: {ex.Message}");
            return new SavedConfigLoadResult(new AppConfig(), "Saved config couldn't be read. Re-enter connection if needed.");
        }
        catch (UnauthorizedAccessException ex)
        {
            DebugLog.Write($"Settings load failed: access denied: {ex.Message}");
            return new SavedConfigLoadResult(new AppConfig(), "Saved config couldn't be opened. Re-enter connection if needed.");
        }
        catch (InvalidOperationException ex)
        {
            DebugLog.Write($"Settings load failed: protected secret unreadable: {ex.Message}");
            return new SavedConfigLoadResult(new AppConfig(), "Saved credentials couldn't be read. Re-enter connection and save again.");
        }
    }

    /// <inheritdoc/>
    public void Save(AppConfig config)
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (directory is null)
        {
            throw new InvalidOperationException("Config directory missing.");
        }

        Directory.CreateDirectory(directory);
        var persisted = PersistedAppConfig.FromAppConfig(config, _secretProtector);
        var json = JsonSerializer.Serialize(persisted, AppJsonSerializerContext.Default.PersistedAppConfig);
        File.WriteAllText(_configPath, json);
    }
}

public sealed record SavedConfigLoadResult(AppConfig Config, string? WarningMessage);
