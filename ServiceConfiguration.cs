using DropAndForget.Services.BucketActions;
using DropAndForget.Services.BucketContent;
using DropAndForget.Services.Cloudflare;
using DropAndForget.Services.Config;
using DropAndForget.Services.ConnectionWorkflow;
using DropAndForget.Services.Encryption;
using DropAndForget.Services.MainWindow;
using DropAndForget.Services.Sync;
using DropAndForget.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DropAndForget;

/// <summary>
/// Configures dependency injection registrations.
/// </summary>
public static class ServiceConfiguration
{
    /// <summary>
    /// Builds the app service provider.
    /// </summary>
    public static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<LocalSecretProtector>();
        services.AddSingleton<JsonSettingsStore>();
        services.AddSingleton<AppConfigValidator>();
        services.AddSingleton<ConnectionWorkflowService>();
        services.AddSingleton<R2ConnectionValidator>();
        services.AddSingleton<R2BucketQueryService>();
        services.AddSingleton<R2BucketTransferService>();
        services.AddSingleton<R2BucketMutationService>();
        services.AddSingleton<R2BucketService>();
        services.AddSingleton<IR2BucketService>(provider => provider.GetRequiredService<R2BucketService>());
        services.AddSingleton<IEncryptedBucketService, EncryptedBucketService>();
        services.AddSingleton<BucketActionWorkflow>();
        services.AddSingleton<BucketContentService>();
        services.AddSingleton<MainWindowConnectionService>();
        services.AddSingleton<MainWindowBucketActionService>();
        services.AddSingleton<SyncStateStore>();
        services.AddSingleton<ISyncModeService, SyncModeService>();
        services.AddSingleton<IStorageModeCoordinator, StorageModeCoordinator>();
        services.AddSingleton<ILocalSyncBrowser, LocalSyncBrowser>();

        services.AddSingleton<ConnectionSetupViewModel>();
        services.AddTransient<BucketBrowserViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
