using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;

namespace DropAndForget.Services.Cloudflare;

internal static class R2BucketClientOps
{
    internal static async Task<List<S3Object>> ListObjectsAsync(
        IAmazonS3 client,
        string bucketName,
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = bucketName,
            Prefix = prefix,
            MaxKeys = 1000
        };

        var objects = new List<S3Object>();

        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request, cancellationToken);
            objects.AddRange(response.S3Objects ?? []);
            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated ?? false);

        return objects;
    }

    internal static async Task<ListObjectsV2Response> ListCurrentLevelAsync(
        IAmazonS3 client,
        string bucketName,
        string? prefix,
        CancellationToken cancellationToken = default)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = bucketName,
            Prefix = prefix,
            Delimiter = "/",
            MaxKeys = 1000
        };

        var objects = new List<S3Object>();
        var folders = new HashSet<string>(System.StringComparer.Ordinal);

        ListObjectsV2Response? lastResponse = null;

        do
        {
            lastResponse = await client.ListObjectsV2Async(request, cancellationToken);
            objects.AddRange(lastResponse.S3Objects ?? []);

            foreach (var folder in lastResponse.CommonPrefixes ?? [])
            {
                folders.Add(folder);
            }

            request.ContinuationToken = lastResponse.NextContinuationToken;
        }
        while (lastResponse.IsTruncated ?? false);

        return new ListObjectsV2Response
        {
            S3Objects = objects,
            CommonPrefixes = new List<string>(folders)
        };
    }

    internal static async Task DownloadObjectAsync(
        IAmazonS3 client,
        string bucketName,
        string objectKey,
        Stream destination,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey
        }, cancellationToken);

        await response.ResponseStream.CopyToAsync(destination, cancellationToken);
    }

    internal static async Task UploadBytesAsync(
        IAmazonS3 client,
        string bucketName,
        string objectKey,
        byte[] bytes,
        string? contentType,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(bytes, writable: false);
        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            InputStream = stream,
            AutoCloseStream = false,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType
        };

        await client.PutObjectAsync(request, cancellationToken);
    }
}
