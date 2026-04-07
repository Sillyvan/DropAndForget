using System.Text;
using DropAndForget.Models;
using DropAndForget.Services.BucketContent;
using DropAndForget.Services.Cloudflare;
using DropAndForget.Services.Encryption;
using DropAndForget.Services.Sync;
using DropAndForget.Tests.TestSupport;
using FluentAssertions;
using Moq;

namespace DropAndForget.Tests.BucketContent;

public sealed class BucketContentServiceTests
{
    [Fact]
    public async Task ListAsync_ShouldUseEncryptedServiceWhenEncryptionEnabled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new[] { new BucketItem { Key = "secret.txt", DisplayName = "secret.txt" } };
        var bucketService = new Mock<IR2BucketService>(MockBehavior.Strict);
        var encryptedBucketService = new Mock<IEncryptedBucketService>(MockBehavior.Strict);
        var localSyncBrowser = new Mock<ILocalSyncBrowser>(MockBehavior.Strict);
        var storageModeCoordinator = new Mock<IStorageModeCoordinator>(MockBehavior.Strict);
        encryptedBucketService
            .Setup(service => service.ListAsync(It.IsAny<AppConfig>(), "docs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var subject = new BucketContentService(bucketService.Object, encryptedBucketService.Object, localSyncBrowser.Object, storageModeCoordinator.Object);
        var config = TestAppConfigFactory.Create(isEncryptionEnabled: true, encryptionBootstrapCompleted: true);

        var items = await subject.ListAsync(config, "docs", cancellationToken);

        items.Should().BeEquivalentTo(expected);
        encryptedBucketService.VerifyAll();
        bucketService.VerifyNoOtherCalls();
        localSyncBrowser.VerifyNoOtherCalls();
        storageModeCoordinator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadFileAsync_ShouldCopyIntoSyncFolderWhenSyncModeEnabled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucketService = new Mock<IR2BucketService>(MockBehavior.Strict);
        var encryptedBucketService = new Mock<IEncryptedBucketService>(MockBehavior.Strict);
        var localSyncBrowser = new Mock<ILocalSyncBrowser>(MockBehavior.Strict);
        var storageModeCoordinator = new Mock<IStorageModeCoordinator>(MockBehavior.Strict);
        storageModeCoordinator
            .Setup(service => service.CopyIntoSyncFolderAsync(It.IsAny<AppConfig>(), It.Is<IReadOnlyList<string>>(paths => paths.Count == 1 && paths[0] == "/tmp/file.txt"), "docs", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var subject = new BucketContentService(bucketService.Object, encryptedBucketService.Object, localSyncBrowser.Object, storageModeCoordinator.Object);
        var config = TestAppConfigFactory.Create(storageMode: StorageMode.Sync, syncFolderPath: "/tmp/sync");

        var objectKey = await subject.UploadFileAsync(config, "/tmp/file.txt", "docs", "nested/file.txt", cancellationToken);

        objectKey.Should().Be("docs/nested/file.txt");
        storageModeCoordinator.VerifyAll();
        bucketService.VerifyNoOtherCalls();
        encryptedBucketService.VerifyNoOtherCalls();
        localSyncBrowser.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DownloadBytesAsync_ShouldUseCloudBucketServiceWhenNeitherSyncNorEncryptionIsEnabled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucketService = new Mock<IR2BucketService>(MockBehavior.Strict);
        var encryptedBucketService = new Mock<IEncryptedBucketService>(MockBehavior.Strict);
        var localSyncBrowser = new Mock<ILocalSyncBrowser>(MockBehavior.Strict);
        var storageModeCoordinator = new Mock<IStorageModeCoordinator>(MockBehavior.Strict);
        bucketService
            .Setup(service => service.DownloadBytesAsync(It.IsAny<AppConfig>(), "report.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("report"));

        var subject = new BucketContentService(bucketService.Object, encryptedBucketService.Object, localSyncBrowser.Object, storageModeCoordinator.Object);
        var config = TestAppConfigFactory.Create();

        var bytes = await subject.DownloadBytesAsync(config, new BucketItem { Key = "report.txt", DisplayName = "report.txt" }, cancellationToken);

        Encoding.UTF8.GetString(bytes).Should().Be("report");
        bucketService.VerifyAll();
        encryptedBucketService.VerifyNoOtherCalls();
        localSyncBrowser.VerifyNoOtherCalls();
        storageModeCoordinator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MoveAsync_ShouldUseSyncBrowserWhenSyncModeEnabled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucketService = new Mock<IR2BucketService>(MockBehavior.Strict);
        var encryptedBucketService = new Mock<IEncryptedBucketService>(MockBehavior.Strict);
        var localSyncBrowser = new Mock<ILocalSyncBrowser>(MockBehavior.Strict);
        var storageModeCoordinator = new Mock<IStorageModeCoordinator>(MockBehavior.Strict);
        var item = new BucketItem { Key = "root/file.txt", DisplayName = "file.txt" };
        localSyncBrowser
            .Setup(service => service.MoveItem(It.IsAny<AppConfig>(), item, "archive"));

        var subject = new BucketContentService(bucketService.Object, encryptedBucketService.Object, localSyncBrowser.Object, storageModeCoordinator.Object);
        var config = TestAppConfigFactory.Create(storageMode: StorageMode.Sync, syncFolderPath: "/tmp/sync");

        var movedCount = await subject.MoveAsync(config, item, "archive", cancellationToken);

        movedCount.Should().Be(1);
        localSyncBrowser.VerifyAll();
        bucketService.VerifyNoOtherCalls();
        encryptedBucketService.VerifyNoOtherCalls();
        storageModeCoordinator.VerifyNoOtherCalls();
    }
}
