using System;
using System.IO;

namespace DropAndForget.Services.Diagnostics;

public static class DebugLog
{
    private static readonly object SyncRoot = new();
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "DropAndForget-debug.log");
    private static readonly bool IsEnabled = System.Diagnostics.Debugger.IsAttached
        || string.Equals(Environment.GetEnvironmentVariable("DROPANDFORGET_DEBUG_LOG"), "1", StringComparison.Ordinal);

    public static string CurrentLogPath => LogPath;

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";

        if (!IsEnabled)
        {
            System.Diagnostics.Debug.WriteLine(line);
            return;
        }

        lock (SyncRoot)
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }

        System.Diagnostics.Debug.WriteLine(line);
    }
}
