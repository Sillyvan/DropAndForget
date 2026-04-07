using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3.Model;
using DropAndForget.Models;

namespace DropAndForget.Services.Cloudflare;

public sealed class R2BucketMutationService
{
    public async Task<int> DeleteAsync(AppConfig config, BucketItem item, CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);
        var bucketName = config.BucketName.Trim();

        if (!item.IsFolder)
        {
            await client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = item.Key
            }, cancellationToken);

            return 1;
        }

        var objects = await R2BucketClientOps.ListObjectsAsync(client, bucketName, item.Key, cancellationToken);
        if (objects.Count == 0)
        {
            return 0;
        }

        return await DeleteObjectsAsync(client, bucketName, objects.Select(static obj => obj.Key), cancellationToken);
    }

    public async Task DeleteObjectByKeyAsync(
        AppConfig config,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);
        await client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = config.BucketName.Trim(),
            Key = objectKey
        }, cancellationToken);
    }

    public async Task<string> CreateFolderAsync(
        AppConfig config,
        string folderName,
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);
        var normalizedFolderName = folderName.Replace('\\', '/').Trim('/');
        var normalizedPrefix = R2BucketPathHelper.NormalizePrefix(prefix);
        var folderKey = string.IsNullOrWhiteSpace(normalizedFolderName)
            ? normalizedPrefix
            : normalizedPrefix + normalizedFolderName + "/";

        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = config.BucketName.Trim(),
            Key = folderKey,
            ContentBody = string.Empty,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        }, cancellationToken);

        return folderKey;
    }

    public async Task<int> RenameAsync(
        AppConfig config,
        BucketItem item,
        string newDisplayName,
        CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);
        var bucketName = config.BucketName.Trim();
        var targetKey = R2BucketPathHelper.BuildRenamedKey(item, newDisplayName);

        if (!item.IsFolder)
        {
            await CopyObjectAsync(client, bucketName, item.Key, targetKey, cancellationToken);
            await DeleteObjectAsync(client, bucketName, item.Key, cancellationToken);

            return 1;
        }

        var objects = await R2BucketClientOps.ListObjectsAsync(client, bucketName, item.Key, cancellationToken);
        if (objects.Count == 0)
        {
            return 0;
        }

        foreach (var obj in objects)
        {
            var suffix = obj.Key[item.Key.Length..];
            await CopyObjectAsync(client, bucketName, obj.Key, targetKey + suffix, cancellationToken);
        }

        return await DeleteObjectsAsync(client, bucketName, objects.Select(static obj => obj.Key), cancellationToken);
    }

    public async Task<int> MoveAsync(
        AppConfig config,
        BucketItem item,
        string targetFolderPath,
        CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);
        var bucketName = config.BucketName.Trim();
        var targetKey = R2BucketPathHelper.BuildMovedKey(item, targetFolderPath);

        if (!item.IsFolder)
        {
            await CopyObjectAsync(client, bucketName, item.Key, targetKey, cancellationToken);
            await DeleteObjectAsync(client, bucketName, item.Key, cancellationToken);

            return 1;
        }

        var objects = await R2BucketClientOps.ListObjectsAsync(client, bucketName, item.Key, cancellationToken);
        if (objects.Count == 0)
        {
            return 0;
        }

        foreach (var obj in objects)
        {
            var suffix = obj.Key[item.Key.Length..];
            await CopyObjectAsync(client, bucketName, obj.Key, targetKey + suffix, cancellationToken);
        }

        return await DeleteObjectsAsync(client, bucketName, objects.Select(static obj => obj.Key), cancellationToken);
    }

    private static Task DeleteObjectAsync(Amazon.S3.AmazonS3Client client, string bucketName, string objectKey, CancellationToken cancellationToken)
    {
        return client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey
        }, cancellationToken);
    }

    private static Task CopyObjectAsync(Amazon.S3.AmazonS3Client client, string bucketName, string sourceKey, string destinationKey, CancellationToken cancellationToken)
    {
        return client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = bucketName,
            SourceKey = sourceKey,
            DestinationBucket = bucketName,
            DestinationKey = destinationKey
        }, cancellationToken);
    }

    private static async Task<int> DeleteObjectsAsync(Amazon.S3.AmazonS3Client client, string bucketName, IEnumerable<string> objectKeys, CancellationToken cancellationToken)
    {
        var deletedCount = 0;

        foreach (var batch in objectKeys.Chunk(1000))
        {
            var response = await client.DeleteObjectsAsync(new DeleteObjectsRequest
            {
                BucketName = bucketName,
                Objects = batch
                    .Select(static key => new KeyVersion { Key = key })
                    .ToList()
            }, cancellationToken);

            deletedCount += response.DeletedObjects.Count;
        }

        return deletedCount;
    }
}
