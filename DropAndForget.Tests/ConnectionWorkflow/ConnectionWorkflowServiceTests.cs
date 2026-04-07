using DropAndForget.Models;
using DropAndForget.Services.Cloudflare;
using DropAndForget.Services.Config;
using DropAndForget.Services.ConnectionWorkflow;
using DropAndForget.Services.Encryption;
using DropAndForget.Tests.TestSupport;
using FluentAssertions;
using Moq;

namespace DropAndForget.Tests.ConnectionWorkflow;

public sealed class ConnectionWorkflowServiceTests
{
    [Fact]
    public async Task PrepareAsync_ShouldValidateConnectionAndClearEncryptionStateForPlainCloudMode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var encryptedBucketService = new Mock<IEncryptedBucketService>(MockBehavior.Strict);
        encryptedBucketService
            .Setup(service => service.GetRemoteStateAsync(It.IsAny<AppConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EncryptedBucketRemoteState.Plain);
        encryptedBucketService.Setup(service => service.Lock());

        var connectionValidator = new StubConnectionValidator();
        var subject = new ConnectionWorkflowService(new AppConfigValidator(), connectionValidator, encryptedBucketService.Object);
        var config = TestAppConfigFactory.Create(isEncryptionEnabled: false, encryptionBootstrapCompleted: true);

        var result = await subject.PrepareAsync(config, string.Empty, string.Empty, encryptionBootstrapCompleted: true, requireConnectionTest: true, cancellationToken);

        result.EncryptionBootstrapCompleted.Should().BeFalse();
        result.ClearSetupPassphrases.Should().BeFalse();
        result.Config.EncryptionBootstrapCompleted.Should().BeFalse();
        connectionValidator.CallCount.Should().Be(1);
        encryptedBucketService.Verify(service => service.GetRemoteStateAsync(It.IsAny<AppConfig>(), It.IsAny<CancellationToken>()), Times.Once);
        encryptedBucketService.Verify(service => service.Lock(), Times.Once);
        encryptedBucketService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PrepareAsync_ShouldInitializeEncryptedBucketOnFirstBootstrap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var encryptedBucketService = new Mock<IEncryptedBucketService>(MockBehavior.Strict);
        encryptedBucketService
            .Setup(service => service.GetRemoteStateAsync(It.IsAny<AppConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EncryptedBucketRemoteState.Plain);
        encryptedBucketService
            .Setup(service => service.InitializeAsync(It.IsAny<AppConfig>(), "supersecret", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var subject = new ConnectionWorkflowService(new AppConfigValidator(), new StubConnectionValidator(), encryptedBucketService.Object);
        var config = TestAppConfigFactory.Create(isEncryptionEnabled: true, storageMode: StorageMode.Sync);

        var result = await subject.PrepareAsync(config, "supersecret", "supersecret", encryptionBootstrapCompleted: false, requireConnectionTest: false, cancellationToken);

        result.EncryptionBootstrapCompleted.Should().BeTrue();
        result.ClearSetupPassphrases.Should().BeTrue();
        result.Config.StorageMode.Should().Be(StorageMode.Cloud);
        result.Config.EncryptionBootstrapCompleted.Should().BeTrue();
        encryptedBucketService.VerifyAll();
    }

    [Fact]
    public async Task PrepareAsync_ShouldLockExistingEncryptedBucketInsteadOfReinitializing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var encryptedBucketService = new Mock<IEncryptedBucketService>(MockBehavior.Strict);
        encryptedBucketService
            .Setup(service => service.GetRemoteStateAsync(It.IsAny<AppConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EncryptedBucketRemoteState.Encrypted);
        encryptedBucketService.Setup(service => service.Lock());

        var subject = new ConnectionWorkflowService(new AppConfigValidator(), new StubConnectionValidator(), encryptedBucketService.Object);
        var config = TestAppConfigFactory.Create(isEncryptionEnabled: false, encryptionBootstrapCompleted: false);

        var result = await subject.PrepareAsync(config, string.Empty, string.Empty, encryptionBootstrapCompleted: false, requireConnectionTest: false, cancellationToken);

        result.EncryptionBootstrapCompleted.Should().BeTrue();
        result.IsEncryptedLocked.Should().BeTrue();
        result.ClearSetupPassphrases.Should().BeTrue();
        result.Config.IsEncryptionEnabled.Should().BeTrue();
        result.Config.EncryptionBootstrapCompleted.Should().BeTrue();
        result.Config.StorageMode.Should().Be(StorageMode.Cloud);
        encryptedBucketService.Verify(service => service.InitializeAsync(It.IsAny<AppConfig>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        encryptedBucketService.VerifyAll();
    }

    [Fact]
    public async Task PrepareAsync_ShouldSkipConnectionTestWhenNotRequired()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var encryptedBucketService = new Mock<IEncryptedBucketService>(MockBehavior.Strict);
        encryptedBucketService
            .Setup(service => service.GetRemoteStateAsync(It.IsAny<AppConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EncryptedBucketRemoteState.Plain);
        encryptedBucketService.Setup(service => service.Lock());

        var connectionValidator = new StubConnectionValidator();
        var subject = new ConnectionWorkflowService(new AppConfigValidator(), connectionValidator, encryptedBucketService.Object);
        var config = TestAppConfigFactory.Create();

        await subject.PrepareAsync(config, string.Empty, string.Empty, encryptionBootstrapCompleted: false, requireConnectionTest: false, cancellationToken);

        connectionValidator.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ChangePassphraseAsync_ShouldRejectShortPassphraseBeforeCallingEncryptedService()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var encryptedBucketService = new Mock<IEncryptedBucketService>(MockBehavior.Strict);
        var subject = new ConnectionWorkflowService(new AppConfigValidator(), new StubConnectionValidator(), encryptedBucketService.Object);
        var config = TestAppConfigFactory.Create(isEncryptionEnabled: true, encryptionBootstrapCompleted: true);

        Func<Task> act = () => subject.ChangePassphraseAsync(config, "old-pass", "short", "short", cancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("New passphrase must be at least 8 chars.");
        encryptedBucketService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteEncryptedBucketAsync_ShouldCallEncryptedService()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var encryptedBucketService = new Mock<IEncryptedBucketService>(MockBehavior.Strict);
        encryptedBucketService
            .Setup(service => service.DeleteBucketAsync(It.IsAny<AppConfig>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var subject = new ConnectionWorkflowService(new AppConfigValidator(), new StubConnectionValidator(), encryptedBucketService.Object);
        var config = TestAppConfigFactory.Create(isEncryptionEnabled: true, encryptionBootstrapCompleted: true);

        await subject.DeleteEncryptedBucketAsync(config, cancellationToken);

        encryptedBucketService.VerifyAll();
    }

    private sealed class StubConnectionValidator : R2ConnectionValidator
    {
        public int CallCount { get; private set; }

        public override Task<string> ValidateAsync(AppConfig config, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult("ok");
        }
    }
}
