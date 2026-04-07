using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace DropAndForget;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = Program.Services.GetRequiredService<MainWindow>();
            mainWindow.Icon = AppIconAssets.CreateWindowIcon();
            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) => Program.DisposeServices();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
