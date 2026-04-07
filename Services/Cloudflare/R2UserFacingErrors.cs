using System;
using System.Threading.Tasks;
using Amazon.Runtime;

namespace DropAndForget.Services.Cloudflare;

internal static class R2UserFacingErrors
{
    internal static async Task ExecuteAsync(Func<Task> action, string fallbackMessage)
    {
        try
        {
            await action();
        }
        catch (AmazonClientException ex)
        {
            throw Wrap(ex, fallbackMessage);
        }
        catch (AmazonServiceException ex)
        {
            throw Wrap(ex, fallbackMessage);
        }
    }

    internal static async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string fallbackMessage)
    {
        try
        {
            return await action();
        }
        catch (AmazonClientException ex)
        {
            throw Wrap(ex, fallbackMessage);
        }
        catch (AmazonServiceException ex)
        {
            throw Wrap(ex, fallbackMessage);
        }
    }

    private static InvalidOperationException Wrap(Exception ex, string fallbackMessage)
    {
        var message = string.IsNullOrWhiteSpace(ex.Message)
            ? fallbackMessage
            : ex.Message;
        return new InvalidOperationException(message, ex);
    }
}
