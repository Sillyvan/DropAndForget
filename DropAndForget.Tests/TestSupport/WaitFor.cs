using FluentAssertions;

namespace DropAndForget.Tests.TestSupport;

internal static class WaitFor
{
    public static async Task EventuallyAsync(Func<bool> condition, TimeSpan timeout, string because)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        condition().Should().BeTrue(because);
    }
}
