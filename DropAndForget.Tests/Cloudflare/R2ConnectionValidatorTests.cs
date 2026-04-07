using DropAndForget.Services.Cloudflare;
using FluentAssertions;

namespace DropAndForget.Tests.Cloudflare;

public sealed class R2ConnectionValidatorTests
{
    [Fact]
    public void NormalizeEndpoint_ShouldConvertAccountIdToR2Endpoint()
    {
        var endpoint = R2ConnectionValidator.NormalizeEndpoint(" account-id ");

        endpoint.Should().Be("https://account-id.r2.cloudflarestorage.com");
    }

    [Fact]
    public void NormalizeEndpoint_ShouldTrimAbsoluteUrlToAuthority()
    {
        var endpoint = R2ConnectionValidator.NormalizeEndpoint("https://example.com/some/path?x=1");

        endpoint.Should().Be("https://example.com");
    }

    [Fact]
    public void NormalizeEndpoint_ShouldRejectBlankValue()
    {
        var act = () => R2ConnectionValidator.NormalizeEndpoint("  ");

        act.Should().Throw<InvalidDataException>()
            .WithMessage("Missing endpoint.");
    }
}
