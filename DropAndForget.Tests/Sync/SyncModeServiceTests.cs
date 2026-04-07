using System.Text;
using DropAndForget.Models;
using DropAndForget.Services.Sync;
using DropAndForget.Tests.TestDoubles;
using DropAndForget.Tests.TestSupport;
using FluentAssertions;

namespace DropAndForget.Tests.Sync;

public sealed class SyncModeServiceTests
{
    [Fact]
    public async Task StartAsync_ShouldBootstrapRemoteSnapshotIntoEmptyFolder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temporaryDirectory = new TemporaryDirectory();
        var bucketService = new InMemoryR2BucketService();
        var stateStore = new SyncStateStore(temporaryDirectory.GetPath("state"));
        var subject = new SyncModeService(bucketService, stateStore, TimeSpan.FromMilliseconds(50), TimeSpan.FromHours(1), TimeSpan.FromMilliseconds(100));
        var config = TestAppConfigFactory.Create(storageMode: StorageMode.Sync, syncFolderPath: temporaryDirectory.GetPath("sync"));
        bucketService.PutObject("docs/", []);
        bucketService.PutObject("docs/report.txt", Encoding.UTF8.GetBytes("remote report"));

        try
        {
            await subject.StartAsync(config, cancellationToken);

            var localFile = temporaryDirectory.GetPath("sync/docs/report.txt");
            File.Exists(localFile).Should().BeTrue();
            (await File.ReadAllTextAsync(localFile, cancellationToken)).Should().Be("remote report");
            subject.GetVisualState("docs/report.txt").Should().Be(SyncVisualState.Synced);
        }
        finally
        {
            await subject.StopAsync();
        }
    }

    [Fact]
    public async Task StartAsync_ShouldRejectNonEmptyFolderBeforeFirstSync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temporaryDirectory = new TemporaryDirectory();
        var syncRoot = temporaryDirectory.GetPath("sync");
        Directory.CreateDirectory(syncRoot);
        await File.WriteAllTextAsync(Path.Combine(syncRoot, "local.txt"), "local only", cancellationToken);
        var subject = new SyncModeService(new InMemoryR2BucketService(), new SyncStateStore(temporaryDirectory.GetPath("state")));
        var config = TestAppConfigFactory.Create(storageMode: StorageMode.Sync, syncFolderPath: syncRoot);

        Func<Task> act = () => subject.StartAsync(config, cancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Sync folder must be empty before first sync.");
    }

    [Fact]
    public async Task ReconcileNowAsync_ShouldMarkConflictWhenLocalAndRemoteChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temporaryDirectory = new TemporaryDirectory();
        var bucketService = new InMemoryR2BucketService();
        var stateStore = new SyncStateStore(temporaryDirectory.GetPath("state"));
        var subject = new SyncModeService(bucketService, stateStore, TimeSpan.FromMilliseconds(50), TimeSpan.FromHours(1), TimeSpan.FromMilliseconds(100));
        var config = TestAppConfigFactory.Create(storageMode: StorageMode.Sync, syncFolderPath: temporaryDirectory.GetPath("sync"));
        bucketService.PutObject("report.txt", Encoding.UTF8.GetBytes("remote-v1"));

        try
        {
            await subject.StartAsync(config, cancellationToken);
            var localFile = temporaryDirectory.GetPath("sync/report.txt");
            await File.WriteAllTextAsync(localFile, "local edit", cancellationToken);
            File.SetLastWriteTimeUtc(localFile, DateTime.UtcNow.AddSeconds(5));
            bucketService.PutObject("report.txt", Encoding.UTF8.GetBytes("remote-v2"));

            await subject.ReconcileNowAsync(cancellationToken);

            subject.GetVisualState("report.txt").Should().Be(SyncVisualState.Pending);
            (await File.ReadAllTextAsync(localFile, cancellationToken)).Should().Be("local edit");
        }
        finally
        {
            await subject.StopAsync();
        }
    }

    [Fact]
    public async Task StartAsync_ShouldUploadNewLocalFileViaWatcher()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temporaryDirectory = new TemporaryDirectory();
        var bucketService = new InMemoryR2BucketService();
        var stateStore = new SyncStateStore(temporaryDirectory.GetPath("state"));
        var subject = new SyncModeService(bucketService, stateStore, TimeSpan.FromMilliseconds(50), TimeSpan.FromHours(1), TimeSpan.FromMilliseconds(100));
        var config = TestAppConfigFactory.Create(storageMode: StorageMode.Sync, syncFolderPath: temporaryDirectory.GetPath("sync"));

        try
        {
            await subject.StartAsync(config, cancellationToken);
            var localFile = temporaryDirectory.GetPath("sync/new-file.txt");
            await File.WriteAllTextAsync(localFile, "upload me", cancellationToken);

            await WaitFor.EventuallyAsync(() => bucketService.ContainsObject("new-file.txt"), TimeSpan.FromSeconds(5), "watcher should upload new local files");
            await WaitFor.EventuallyAsync(() => subject.GetVisualState("new-file.txt") == SyncVisualState.Synced, TimeSpan.FromSeconds(5), "watcher should settle new local files to synced state");

            Encoding.UTF8.GetString(bucketService.ReadObject("new-file.txt")).Should().Be("upload me");
            subject.GetVisualState("new-file.txt").Should().Be(SyncVisualState.Synced);
        }
        finally
        {
            await subject.StopAsync();
        }
    }
}
