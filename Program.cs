using Avalonia;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using DropAndForget.Services.Diagnostics;

namespace DropAndForget;

/// <summary>
/// App entry point.
/// </summary>
public static class Program
{
    private static bool _servicesDisposed;

    /// <summary>
    /// Gets the root service provider.
    /// </summary>
    public static ServiceProvider Services { get; } = ServiceConfiguration.BuildServiceProvider();

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                DebugLog.Write($"Unhandled exception: {ex}");
                return;
            }

            DebugLog.Write($"Unhandled exception object: {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DebugLog.Write($"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Builds the Avalonia app.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    public static void DisposeServices()
    {
        if (_servicesDisposed)
        {
            return;
        }

        Services.Dispose();
        _servicesDisposed = true;
    }
}
