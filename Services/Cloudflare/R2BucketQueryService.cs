using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using DropAndForget.Models;

namespace DropAndForget.Services.Cloudflare;

public sealed class R2BucketQueryService
{
    public async Task<IReadOnlyList<R2ObjectInfo>> ListAllObjectsAsync(
        AppConfig config,
        CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);
        var objects = await R2BucketClientOps.ListObjectsAsync(client, config.BucketName.Trim(), prefix: null, cancellationToken);

        return objects
            .Where(obj => !string.IsNullOrWhiteSpace(obj.Key))
            .Select(R2BucketMapper.CreateObjectInfo)
            .ToList();
    }

    public async Task<R2ObjectInfo?> HeadObjectAsync(
        AppConfig config,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);

        try
        {
            var response = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = config.BucketName.Trim(),
                Key = objectKey
            }, cancellationToken);

            return new R2ObjectInfo
            {
                Key = objectKey,
                IsFolder = objectKey.EndsWith("/", StringComparison.Ordinal),
                Size = response.Headers.ContentLength,
                LastModifiedUtc = (response.LastModified ?? DateTime.MinValue).ToUniversalTime(),
                ETag = response.ETag ?? string.Empty
            };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<BucketItem>> ListAsync(
        AppConfig config,
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);
        var normalizedPrefix = R2BucketPathHelper.NormalizePrefix(prefix);
        var response = await R2BucketClientOps.ListCurrentLevelAsync(
            client,
            config.BucketName.Trim(),
            normalizedPrefix,
            cancellationToken);

        return R2BucketMapper.BuildBucketItems(response, normalizedPrefix);
    }

    public async Task<IReadOnlyList<BucketItem>> SearchAsync(
        AppConfig config,
        string term,
        CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);
        var normalizedTerm = term.Trim();

        if (string.IsNullOrWhiteSpace(normalizedTerm))
        {
            return [];
        }

        var objects = await R2BucketClientOps.ListObjectsAsync(
            client,
            config.BucketName.Trim(),
            prefix: null,
            cancellationToken);

        return objects
            .Where(obj => !string.IsNullOrWhiteSpace(obj.Key)
                && !obj.Key.EndsWith("/", StringComparison.Ordinal)
                && obj.Key.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase))
            .Select(obj => R2BucketMapper.CreateFileBucketItem(obj))
            .OrderBy(item => item.FolderPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
