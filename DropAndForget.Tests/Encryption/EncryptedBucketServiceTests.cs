using System.IO.Compression;
using System.Text;
using DropAndForget.Models;
using DropAndForget.Services.Encryption;
using DropAndForget.Tests.TestDoubles;
using DropAndForget.Tests.TestSupport;
using FluentAssertions;

namespace DropAndForget.Tests.Encryption;

public sealed class EncryptedBucketServiceTests
{
    [Fact]
    public async Task EncryptedBucketService_ShouldRoundTripEncryptedContentAndFolderOperations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucketService = new InMemoryR2BucketService();
        var subject = new EncryptedBucketService(bucketService);
        var config = TestAppConfigFactory.Create(isEncryptionEnabled: true);

        await subject.InitializeAsync(config, "supersecret", cancellationToken);
        config.EncryptionBootstrapCompleted = true;

        await subject.CreateFolderAsync(config, "docs", cancellationToken: cancellationToken);

        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("note.txt");
        await File.WriteAllTextAsync(filePath, "hello encrypted world", cancellationToken);
        await subject.UploadFileAsync(config, filePath, "docs", cancellationToken: cancellationToken);

        var rootItems = await subject.ListAsync(config, cancellationToken: cancellationToken);
        var docsFolder = rootItems.Should().ContainSingle(item => item.IsFolder && item.DisplayName == "docs").Subject;
        var folderItems = await subject.ListAsync(config, "docs", cancellationToken);
        var uploadedFile = folderItems.Should().ContainSingle(item => item.IsFile && item.DisplayName == "note.txt").Subject;

        var downloadedBytes = await subject.DownloadBytesAsync(config, uploadedFile.Key, cancellationToken);
        Encoding.UTF8.GetString(downloadedBytes).Should().Be("hello encrypted world");

        using var zipStream = new MemoryStream();
        await subject.DownloadFolderAsZipAsync(config, docsFolder, zipStream, cancellationToken);
        zipStream.Position = 0;
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.Entries.Select(entry => entry.FullName).Should().Contain("docs/note.txt");
        }

        var renamedCount = await subject.RenameAsync(config, uploadedFile, "renamed.txt", cancellationToken);
        renamedCount.Should().Be(1);
        var renamedFile = (await subject.ListAsync(config, "docs", cancellationToken)).Should().ContainSingle(item => item.DisplayName == "renamed.txt").Subject;

        await subject.CreateFolderAsync(config, "archive", cancellationToken: cancellationToken);
        var archiveFolder = (await subject.ListAsync(config, cancellationToken: cancellationToken)).Should().ContainSingle(item => item.IsFolder && item.DisplayName == "archive").Subject;
        var movedCount = await subject.MoveAsync(config, renamedFile, archiveFolder.Key, cancellationToken);
        movedCount.Should().Be(1);
        var movedFile = (await subject.ListAsync(config, "archive", cancellationToken)).Should().ContainSingle(item => item.DisplayName == "renamed.txt").Subject;

        var deletedCount = await subject.DeleteAsync(config, movedFile, cancellationToken);
        deletedCount.Should().Be(1);
        (await subject.ListAsync(config, "archive", cancellationToken)).Should().BeEmpty();
        bucketService.GetObjectKeys().Should().Contain([".daf/crypto.json", ".daf/index.enc"]);
    }

    [Fact]
    public async Task UnlockAsync_ShouldFailForWrongPassphraseAndLeaveServiceLocked()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucketService = new InMemoryR2BucketService();
        var setupService = new EncryptedBucketService(bucketService);
        var config = TestAppConfigFactory.Create(isEncryptionEnabled: true, encryptionBootstrapCompleted: true);
        await setupService.InitializeAsync(config, "supersecret", cancellationToken);
        setupService.Lock();

        var subject = new EncryptedBucketService(bucketService);
        Func<Task> act = () => subject.UnlockAsync(config, "wrong-passphrase", cancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        subject.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePassphraseAsync_ShouldPreserveExistingDataForNewPassphrase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucketService = new InMemoryR2BucketService();
        var initialService = new EncryptedBucketService(bucketService);
        var config = TestAppConfigFactory.Create(isEncryptionEnabled: true, encryptionBootstrapCompleted: true);
        await initialService.InitializeAsync(config, "supersecret", cancellationToken);

        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("note.txt");
        await File.WriteAllTextAsync(filePath, "hello encrypted world", cancellationToken);
        await initialService.UploadFileAsync(config, filePath, cancellationToken: cancellationToken);
        initialService.Lock();

        var rotateService = new EncryptedBucketService(bucketService);
        await rotateService.ChangePassphraseAsync(config, "supersecret", "next-passphrase", cancellationToken);
        rotateService.Lock();

        var subject = new EncryptedBucketService(bucketService);
        await subject.UnlockAsync(config, "next-passphrase", cancellationToken);
        var bytes = await subject.DownloadBytesAsync(config, "note.txt", cancellationToken);

        Encoding.UTF8.GetString(bytes).Should().Be("hello encrypted world");
        Func<Task> oldPassAct = () => new EncryptedBucketService(bucketService).UnlockAsync(config, "supersecret", cancellationToken);
        await oldPassAct.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExistingEncryptedBucket_ShouldUnlockOnSecondClientWithSamePassphrase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucketService = new InMemoryR2BucketService();
        var config = TestAppConfigFactory.Create(isEncryptionEnabled: true, encryptionBootstrapCompleted: true);
        var firstClient = new EncryptedBucketService(bucketService);
        await firstClient.InitializeAsync(config, "supersecret", cancellationToken);

        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("note.txt");
        await File.WriteAllTextAsync(filePath, "hello encrypted world", cancellationToken);
        await firstClient.UploadFileAsync(config, filePath, cancellationToken: cancellationToken);
        firstClient.Lock();

        var secondClient = new EncryptedBucketService(bucketService);
        var remoteState = await secondClient.GetRemoteStateAsync(config, cancellationToken);
        await secondClient.UnlockAsync(config, "supersecret", cancellationToken);
        var items = await secondClient.ListAsync(config, cancellationToken: cancellationToken);

        remoteState.Should().Be(EncryptedBucketRemoteState.Encrypted);
        items.Should().ContainSingle(item => item.IsFile && item.DisplayName == "note.txt");
    }

    [Fact]
    public async Task DeleteBucketAsync_ShouldDeleteEveryObject()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucketService = new InMemoryR2BucketService();
        var subject = new EncryptedBucketService(bucketService);
        var config = TestAppConfigFactory.Create(isEncryptionEnabled: true, encryptionBootstrapCompleted: true);
        await subject.InitializeAsync(config, "supersecret", cancellationToken);

        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("note.txt");
        await File.WriteAllTextAsync(filePath, "hello encrypted world", cancellationToken);
        await subject.UploadFileAsync(config, filePath, cancellationToken: cancellationToken);
        bucketService.PutObject("plain.txt", Encoding.UTF8.GetBytes("plain"));

        await subject.DeleteBucketAsync(config, cancellationToken);

        bucketService.GetObjectKeys().Should().BeEmpty();
        (await subject.GetRemoteStateAsync(config, cancellationToken)).Should().Be(EncryptedBucketRemoteState.Plain);
    }

    [Fact]
    public async Task InitializeAsync_ShouldRejectNonEmptyBucket()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bucketService = new InMemoryR2BucketService();
        bucketService.PutObject("plain.txt", Encoding.UTF8.GetBytes("plain"));
        var subject = new EncryptedBucketService(bucketService);
        var config = TestAppConfigFactory.Create(isEncryptionEnabled: true);

        Func<Task> act = () => subject.InitializeAsync(config, "supersecret", cancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Encryption setup needs an empty bucket.");
    }
}
