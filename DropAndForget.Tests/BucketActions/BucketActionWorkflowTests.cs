using System.IO.Compression;
using System.Text;
using DropAndForget.Models;
using DropAndForget.Services.BucketActions;
using DropAndForget.Services.BucketContent;
using DropAndForget.Services.Cloudflare;
using DropAndForget.Services.Encryption;
using DropAndForget.Services.Sync;
using DropAndForget.Tests.TestDoubles;
using DropAndForget.Tests.TestSupport;
using FluentAssertions;
using Moq;

namespace DropAndForget.Tests.BucketActions;

public sealed class BucketActionWorkflowTests
{
    [Fact]
    public async Task DownloadAsZipAsync_ShouldZipMultipleSelectedItems()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucketService = new InMemoryR2BucketService();
        bucketService.PutObject("docs/readme.txt", Encoding.UTF8.GetBytes("readme"));
        bucketService.PutObject("docs/notes/todo.txt", Encoding.UTF8.GetBytes("todo"));
        bucketService.PutObject("report.txt", Encoding.UTF8.GetBytes("report"));
        var subject = CreateSubject(bucketService);
        var config = TestAppConfigFactory.Create();
        await using var zipStream = new MemoryStream();

        await subject.DownloadAsZipAsync(config,
        [
            new BucketItem { Key = "docs", DisplayName = "docs", IsFolder = true },
            new BucketItem { Key = "report.txt", DisplayName = "report.txt" }
        ], zipStream, cancellationToken);

        zipStream.Position = 0;
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        archive.Entries.Select(static entry => entry.FullName).Should().BeEquivalentTo([
            "docs/readme.txt",
            "docs/notes/todo.txt",
            "report.txt"
        ]);
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteMultipleSelectedItems()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucketService = new InMemoryR2BucketService();
        bucketService.PutObject("docs/readme.txt", Encoding.UTF8.GetBytes("readme"));
        bucketService.PutObject("report.txt", Encoding.UTF8.GetBytes("report"));
        var subject = CreateSubject(bucketService);
        var config = TestAppConfigFactory.Create();

        var deletedCount = await subject.DeleteAsync(config,
        [
            new BucketItem { Key = "docs", DisplayName = "docs", IsFolder = true },
            new BucketItem { Key = "report.txt", DisplayName = "report.txt" }
        ], cancellationToken);

        deletedCount.Should().Be(2);
        bucketService.GetObjectKeys().Should().BeEmpty();
    }

    [Fact]
    public async Task MoveAsync_ShouldMoveMultipleSelectedItemsIntoTargetFolder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucketService = new InMemoryR2BucketService();
        bucketService.PutObject("docs/readme.txt", Encoding.UTF8.GetBytes("readme"));
        bucketService.PutObject("report.txt", Encoding.UTF8.GetBytes("report"));
        bucketService.PutObject("archive/", []);
        var subject = CreateSubject(bucketService);
        var config = TestAppConfigFactory.Create();

        await subject.MoveAsync(config,
        [
            new BucketItem { Key = "docs", DisplayName = "docs", IsFolder = true, FolderPath = string.Empty },
            new BucketItem { Key = "report.txt", DisplayName = "report.txt", FolderPath = string.Empty }
        ], "archive", cancellationToken);

        bucketService.GetObjectKeys().Should().BeEquivalentTo([
            "archive/",
            "archive/docs/readme.txt",
            "archive/report.txt"
        ]);
    }

    private static BucketActionWorkflow CreateSubject(InMemoryR2BucketService bucketService)
    {
        var encryptedBucketService = new Mock<IEncryptedBucketService>(MockBehavior.Strict);
        var localSyncBrowser = new Mock<ILocalSyncBrowser>(MockBehavior.Strict);
        var storageModeCoordinator = new Mock<IStorageModeCoordinator>(MockBehavior.Strict);
        var bucketContentService = new BucketContentService(
            bucketService,
            encryptedBucketService.Object,
            localSyncBrowser.Object,
            storageModeCoordinator.Object);

        return new BucketActionWorkflow(bucketContentService, storageModeCoordinator.Object);
    }
}
