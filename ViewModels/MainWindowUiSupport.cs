using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using DropAndForget.Services.Diagnostics;

namespace DropAndForget.ViewModels;

internal static class MainWindowUiSupport
{
    internal static bool IsHandledStatusException(Exception ex)
    {
        return ex is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or CryptographicException
            or JsonException;
    }

    internal static void ObserveBackgroundTask(Task task, string context)
    {
        _ = ObserveBackgroundTaskCoreAsync(task, context);
    }

    internal static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static async Task ObserveBackgroundTaskCoreAsync(Task task, string context)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (IsHandledStatusException(ex))
        {
            DebugLog.Write($"Background task failed while {context}: {ex}");
        }
    }
}
