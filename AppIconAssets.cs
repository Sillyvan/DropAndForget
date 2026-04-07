using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DropAndForget;

internal static class AppIconAssets
{
    private static readonly Uri WindowIconUri = new("avares://DropAndForget/Assets/logo.ico");
    private static Bitmap? _logoBitmap;

    public static WindowIcon CreateWindowIcon()
    {
        using var stream = AssetLoader.Open(WindowIconUri);
        return new WindowIcon(stream);
    }

    public static Image CreateTitleBarLogo(double size = 18)
    {
        return new Image
        {
            Source = GetBitmap(),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
    }

    private static Bitmap GetBitmap()
    {
        _logoBitmap ??= LoadBitmap();
        return _logoBitmap;
    }

    private static Bitmap LoadBitmap()
    {
        using var stream = AssetLoader.Open(WindowIconUri);
        return new Bitmap(stream);
    }
}
