using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;

namespace DropAndForget.Services.Cloudflare;

public sealed class R2BucketTransferService
{
    public async Task DownloadFolderAsZipAsync(
        AppConfig config,
        BucketItem item,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);
        var bucketName = config.BucketName.Trim();
        var objects = await R2BucketClientOps.ListObjectsAsync(client, bucketName, item.Key, cancellationToken);
        var rootName = item.DisplayName.Trim().TrimEnd('/');

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var hasEntries = false;

        foreach (var obj in objects.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(obj.Key) || !obj.Key.StartsWith(item.Key, StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = obj.Key[item.Key.Length..];
            var entryName = string.IsNullOrEmpty(relativePath)
                ? rootName + "/"
                : rootName + "/" + relativePath;

            if (string.IsNullOrEmpty(relativePath) || obj.Key.EndsWith("/", StringComparison.Ordinal))
            {
                archive.CreateEntry(entryName, CompressionLevel.NoCompression);
                hasEntries = true;
                continue;
            }

            var zipEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            await using var zipStream = zipEntry.Open();
            await R2BucketClientOps.DownloadObjectAsync(client, bucketName, obj.Key, zipStream, cancellationToken);
            hasEntries = true;
        }

        if (!hasEntries)
        {
            archive.CreateEntry(rootName + "/", CompressionLevel.NoCompression);
        }
    }

    public async Task DownloadFileAsync(
        AppConfig config,
        string objectKey,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);
        await R2BucketClientOps.DownloadObjectAsync(client, config.BucketName.Trim(), objectKey, destination, cancellationToken);
    }

    public async Task DownloadObjectToFileAsync(
        AppConfig config,
        string objectKey,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(filePath);
        await DownloadFileAsync(config, objectKey, stream, cancellationToken);
    }

    public async Task<string> UploadFileAsync(
        AppConfig config,
        string filePath,
        string? prefix = null,
        string? relativeObjectPath = null,
        CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);
        var objectKey = R2BucketPathHelper.BuildObjectKey(filePath, prefix, relativeObjectPath);

        await client.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
        {
            BucketName = config.BucketName.Trim(),
            Key = objectKey,
            FilePath = filePath,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        }, cancellationToken);

        return objectKey;
    }

    public async Task UploadBytesAsync(
        AppConfig config,
        string objectKey,
        byte[] bytes,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        using var client = R2ClientFactory.CreateClient(config);
        await R2BucketClientOps.UploadBytesAsync(client, config.BucketName.Trim(), objectKey, bytes, contentType, cancellationToken);
    }

    public async Task<byte[]> DownloadBytesAsync(
        AppConfig config,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream();
        await DownloadFileAsync(config, objectKey, stream, cancellationToken);
        return stream.ToArray();
    }

    public async Task<string> DownloadTextAsync(
        AppConfig config,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var bytes = await DownloadBytesAsync(config, objectKey, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
