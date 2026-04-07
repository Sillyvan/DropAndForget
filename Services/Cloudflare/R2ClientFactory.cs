using Amazon.Runtime;
using Amazon.S3;
using DropAndForget.Models;

namespace DropAndForget.Services.Cloudflare;

internal static class R2ClientFactory
{
    internal static AmazonS3Client CreateClient(AppConfig config)
    {
        var credentials = new BasicAWSCredentials(config.AccessKeyId.Trim(), config.SecretAccessKey.Trim());

        return new AmazonS3Client(credentials, new AmazonS3Config
        {
            ServiceURL = R2ConnectionValidator.NormalizeEndpoint(config.EndpointOrAccountId),
            AuthenticationRegion = "auto",
            ForcePathStyle = true
        });
    }
}
