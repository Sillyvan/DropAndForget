using Avalonia.Markup.Xaml;
using Avalonia.Styling;

// ReSharper disable once CheckNamespace
namespace DropAndForget.UI;

/// <summary>
///     The main theme for the application.
/// </summary>
public class AppTheme : Styles
{
    /// <summary>
    ///     Returns a new instance of the <see cref="AppTheme" /> class.
    /// </summary>
    public AppTheme()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
