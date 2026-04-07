using System;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;
using DropAndForget.Services.Cloudflare;
using DropAndForget.Services.Config;
using DropAndForget.Services.Encryption;
using DropAndForget.Services.MainWindow;

namespace DropAndForget.Services.ConnectionWorkflow;

public sealed class ConnectionWorkflowService(
    AppConfigValidator configValidator,
    R2ConnectionValidator connectionValidator,
    IEncryptedBucketService encryptedBucketService)
{
    private readonly AppConfigValidator _configValidator = configValidator;
    private readonly R2ConnectionValidator _connectionValidator = connectionValidator;
    private readonly IEncryptedBucketService _encryptedBucketService = encryptedBucketService;

    public async Task<ConnectionPreparationResult> PrepareAsync(
        AppConfig config,
        string setupPassphrase,
        string confirmSetupPassphrase,
        bool encryptionBootstrapCompleted,
        bool requireConnectionTest,
        CancellationToken cancellationToken = default)
    {
        return await R2UserFacingErrors.ExecuteAsync(async () =>
        {
            _configValidator.ValidateConnectionConfig(config);

            if (requireConnectionTest)
            {
                await _connectionValidator.ValidateAsync(config, cancellationToken);
            }

            var remoteState = await _encryptedBucketService.GetRemoteStateAsync(config, cancellationToken);
            if (remoteState == EncryptedBucketRemoteState.Encrypted)
            {
                ApplyEncryptedCloudState(config);
                _encryptedBucketService.Lock();
                return new ConnectionPreparationResult(config, true, true, true);
            }

            if (!config.IsEncryptionEnabled)
            {
                _encryptedBucketService.Lock();
                ApplyPlainCloudState(config);
                return new ConnectionPreparationResult(config, false, false, false);
            }

            config.StorageMode = StorageMode.Cloud;
            ValidateSetupPassphrase(setupPassphrase, confirmSetupPassphrase);
            await _encryptedBucketService.InitializeAsync(config, setupPassphrase, cancellationToken);
            config.EncryptionBootstrapCompleted = true;
            return new ConnectionPreparationResult(config, true, false, true);
        }, "Couldn't prepare connection.");
    }

    public Task UnlockAsync(AppConfig config, string passphrase, CancellationToken cancellationToken = default)
    {
        return R2UserFacingErrors.ExecuteAsync(() => _encryptedBucketService.UnlockAsync(config, passphrase, cancellationToken), "Couldn't unlock encrypted bucket.");
    }

    public async Task ChangePassphraseAsync(AppConfig config, string currentPassphrase, string nextPassphrase, string confirmNextPassphrase, CancellationToken cancellationToken = default)
    {
        ValidateNextPassphrase(nextPassphrase, confirmNextPassphrase);
        await R2UserFacingErrors.ExecuteAsync(() => _encryptedBucketService.ChangePassphraseAsync(config, currentPassphrase, nextPassphrase, cancellationToken), "Couldn't change passphrase.");
    }

    public Task DeleteEncryptedBucketAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        return R2UserFacingErrors.ExecuteAsync(() => _encryptedBucketService.DeleteBucketAsync(config, cancellationToken), "Couldn't delete encrypted bucket.");
    }

    private static void ValidateSetupPassphrase(string passphrase, string confirmPassphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            throw new InvalidOperationException("Set an encryption passphrase first.");
        }

        if (passphrase.Length < 8)
        {
            throw new InvalidOperationException("Passphrase must be at least 8 chars.");
        }

        if (!string.Equals(passphrase, confirmPassphrase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Passphrase confirmation doesn't match.");
        }
    }

    private static void ValidateNextPassphrase(string passphrase, string confirmPassphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 8)
        {
            throw new InvalidOperationException("New passphrase must be at least 8 chars.");
        }

        if (!string.Equals(passphrase, confirmPassphrase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("New passphrase confirmation doesn't match.");
        }
    }

    private static void ApplyEncryptedCloudState(AppConfig config)
    {
        config.StorageMode = StorageMode.Cloud;
        config.IsEncryptionEnabled = true;
        config.EncryptionBootstrapCompleted = true;
    }

    private static void ApplyPlainCloudState(AppConfig config)
    {
        config.StorageMode = StorageMode.Cloud;
        config.IsEncryptionEnabled = false;
        config.EncryptionBootstrapCompleted = false;
    }
}
