using System.Text.Json;
using DropAndForget.Models;
using DropAndForget.Services.Config;
using DropAndForget.Tests.TestSupport;
using FluentAssertions;

namespace DropAndForget.Tests.Config;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public void Load_ShouldReturnDefaultConfigWhenFileIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = CreateStore(temporaryDirectory);

        var result = store.Load();

        result.Config.Should().BeEquivalentTo(new { BucketName = string.Empty, EndpointOrAccountId = string.Empty, AccessKeyId = string.Empty, SecretAccessKey = string.Empty });
        result.WarningMessage.Should().BeNull();
    }

    [Fact]
    public void SaveAndLoad_ShouldRoundTripPersistedConfig()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var configPath = temporaryDirectory.GetPath("config.json");
        var store = CreateStore(temporaryDirectory);
        var config = new AppConfig
        {
            BucketName = "docs",
            EndpointOrAccountId = "account-id",
            AccessKeyId = "access-key",
            SecretAccessKey = "secret-key",
            SyncFolderPath = "/tmp/sync"
        };

        store.Save(config);
        var loaded = store.Load();

        loaded.Config.Should().BeEquivalentTo(config);
        loaded.WarningMessage.Should().BeNull();

        var persisted = File.ReadAllText(configPath);
        persisted.Should().NotContain("access-key");
        persisted.Should().NotContain("secret-key");
        persisted.Should().Contain("EncryptedAccessKeyId");
        persisted.Should().Contain("EncryptedSecretAccessKey");
    }

    [Fact]
    public void Load_ShouldReturnDefaultConfigWhenJsonIsCorrupted()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var configPath = temporaryDirectory.GetPath("config.json");
        File.WriteAllText(configPath, "{ not-json }");
        var store = CreateStore(temporaryDirectory);

        var config = store.Load();

        config.Config.Should().BeEquivalentTo(new AppConfig());
        config.WarningMessage.Should().Be("Saved config is corrupted. Re-enter connection and save again.");
    }

    [Fact]
    public void Load_ShouldReadExistingPlaintextSecrets()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var configPath = temporaryDirectory.GetPath("config.json");
        var persisted = new PersistedAppConfig
        {
            BucketName = "docs",
            EndpointOrAccountId = "account-id",
            AccessKeyId = "access-key",
            SecretAccessKey = "secret-key",
            SyncFolderPath = "/tmp/sync"
        };

        var json = JsonSerializer.Serialize(persisted);
        File.WriteAllText(configPath, json);

        var store = CreateStore(temporaryDirectory);
        var loaded = store.Load();

        loaded.Config.Should().BeEquivalentTo(new AppConfig
        {
            BucketName = "docs",
            EndpointOrAccountId = "account-id",
            AccessKeyId = "access-key",
            SecretAccessKey = "secret-key",
            SyncFolderPath = "/tmp/sync"
        });
        loaded.WarningMessage.Should().BeNull();
    }

    [Fact]
    public void Load_ShouldWarnWhenEncryptedSecretsCannotBeRead()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var configPath = temporaryDirectory.GetPath("config.json");
        var keyPath = temporaryDirectory.GetPath("config.key");
        var writingProtector = new LocalSecretProtector(keyPath);
        var persisted = PersistedAppConfig.FromAppConfig(new AppConfig
        {
            BucketName = "docs",
            EndpointOrAccountId = "account-id",
            AccessKeyId = "access-key",
            SecretAccessKey = "secret-key"
        }, writingProtector);

        File.WriteAllText(configPath, JsonSerializer.Serialize(persisted));
        File.WriteAllBytes(keyPath, [1, 2, 3]);

        var store = CreateStore(temporaryDirectory);
        var loaded = store.Load();

        loaded.Config.Should().BeEquivalentTo(new AppConfig());
        loaded.WarningMessage.Should().Be("Saved credentials couldn't be read. Re-enter connection and save again.");
    }

    private static JsonSettingsStore CreateStore(TemporaryDirectory temporaryDirectory)
    {
        var protector = new LocalSecretProtector(temporaryDirectory.GetPath("config.key"));
        return new JsonSettingsStore(protector, temporaryDirectory.GetPath("config.json"));
    }
}
