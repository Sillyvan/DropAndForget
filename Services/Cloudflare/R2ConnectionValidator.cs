using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using DropAndForget.Models;

namespace DropAndForget.Services.Cloudflare;

public class R2ConnectionValidator
{
    public virtual async Task<string> ValidateAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);

        var bucketName = config.BucketName.Trim();

        await client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = bucketName,
            MaxKeys = 1
        }, cancellationToken);

        var testKey = $"dropandforget-test/{Guid.NewGuid():N}.txt";

        try
        {
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = testKey,
                ContentBody = "ok",
                DisablePayloadSigning = true,
                DisableDefaultChecksumValidation = true
            }, cancellationToken);
        }
        finally
        {
            try
            {
                await client.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = bucketName,
                    Key = testKey
                }, cancellationToken);
            }
            catch (AmazonS3Exception)
            {
            }
        }

        return $"Connected to {bucketName}.";
    }

    public static string NormalizeEndpoint(string endpointOrAccountId)
    {
        var value = endpointOrAccountId.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("Missing endpoint.");
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }

        return $"https://{value}.r2.cloudflarestorage.com";
    }
}
