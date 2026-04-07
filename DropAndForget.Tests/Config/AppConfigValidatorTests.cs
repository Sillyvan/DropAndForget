using DropAndForget.Models;
using DropAndForget.Services.Config;
using DropAndForget.Tests.TestSupport;
using FluentAssertions;

namespace DropAndForget.Tests.Config;

public sealed class AppConfigValidatorTests
{
    private readonly AppConfigValidator _subject = new();

    [Fact]
    public void ValidateConnectionConfig_ShouldRejectMissingRequiredSettings()
    {
        var config = new AppConfig();

        var act = () => _subject.ValidateConnectionConfig(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Default endpoint required.");
    }

    [Fact]
    public void ValidatePersistableConfig_ShouldRejectEncryptedSyncMode()
    {
        var config = TestAppConfigFactory.Create(storageMode: StorageMode.Sync, isEncryptionEnabled: true, syncFolderPath: "/tmp/sync");

        var act = () => _subject.ValidatePersistableConfig(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Encryption mode can't use sync.");
    }

    [Fact]
    public void ValidatePersistableConfig_ShouldRejectMissingSyncFolderInSyncMode()
    {
        var config = TestAppConfigFactory.Create(storageMode: StorageMode.Sync, syncFolderPath: " ");

        var act = () => _subject.ValidatePersistableConfig(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Pick a sync folder first.");
    }

    [Fact]
    public void ValidatePersistableConfig_ShouldAcceptSyncPathUsingHomePrefix()
    {
        var config = TestAppConfigFactory.Create(storageMode: StorageMode.Sync, syncFolderPath: "~/DropAndForgetSync");

        var act = () => _subject.ValidatePersistableConfig(config);

        act.Should().NotThrow();
    }
}
