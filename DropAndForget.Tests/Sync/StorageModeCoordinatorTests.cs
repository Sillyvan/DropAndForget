using DropAndForget.Models;
using DropAndForget.Services.Config;
using DropAndForget.Services.Sync;
using DropAndForget.Tests.TestSupport;
using FluentAssertions;
using Moq;

namespace DropAndForget.Tests.Sync;

public sealed class StorageModeCoordinatorTests
{
    [Fact]
    public async Task ApplyAsync_ShouldStartSyncWhenConfigUsesSyncMode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var syncModeService = new Mock<ISyncModeService>(MockBehavior.Strict);
        syncModeService
            .Setup(service => service.StartAsync(It.IsAny<AppConfig>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var subject = new StorageModeCoordinator(syncModeService.Object, new AppConfigValidator());
        var config = TestAppConfigFactory.Create(storageMode: StorageMode.Sync, syncFolderPath: "/tmp/sync");

        await subject.ApplyAsync(config, cancellationToken);

        syncModeService.VerifyAll();
    }

    [Fact]
    public async Task ApplyAsync_ShouldStopSyncWhenConfigUsesCloudMode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var syncModeService = new Mock<ISyncModeService>(MockBehavior.Strict);
        syncModeService
            .Setup(service => service.StopAsync())
            .Returns(Task.CompletedTask);

        var subject = new StorageModeCoordinator(syncModeService.Object, new AppConfigValidator());

        await subject.ApplyAsync(TestAppConfigFactory.Create(), cancellationToken);

        syncModeService.VerifyAll();
    }

    [Fact]
    public async Task CopyIntoSyncFolderAsync_ShouldRejectInvalidSyncConfigBeforeDelegating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var syncModeService = new Mock<ISyncModeService>(MockBehavior.Strict);
        var subject = new StorageModeCoordinator(syncModeService.Object, new AppConfigValidator());
        var config = TestAppConfigFactory.Create(storageMode: StorageMode.Sync, syncFolderPath: string.Empty);

        Func<Task> act = () => subject.CopyIntoSyncFolderAsync(config, ["/tmp/file.txt"], string.Empty, cancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Pick a sync folder first.");
        syncModeService.VerifyNoOtherCalls();
    }
}
