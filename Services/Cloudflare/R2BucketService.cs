using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;

namespace DropAndForget.Services.Cloudflare;

public sealed class R2BucketService(
    R2BucketQueryService queryService,
    R2BucketTransferService transferService,
    R2BucketMutationService mutationService) : IR2BucketService
{
    private readonly R2BucketQueryService _queryService = queryService;
    private readonly R2BucketTransferService _transferService = transferService;
    private readonly R2BucketMutationService _mutationService = mutationService;

    public Task<IReadOnlyList<R2ObjectInfo>> ListAllObjectsAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        return _queryService.ListAllObjectsAsync(config, cancellationToken);
    }

    public Task<R2ObjectInfo?> HeadObjectAsync(AppConfig config, string objectKey, CancellationToken cancellationToken = default)
    {
        return _queryService.HeadObjectAsync(config, objectKey, cancellationToken);
    }

    public Task DownloadFolderAsZipAsync(AppConfig config, BucketItem item, Stream destination, CancellationToken cancellationToken = default)
    {
        return _transferService.DownloadFolderAsZipAsync(config, item, destination, cancellationToken);
    }

    public Task DownloadFileAsync(AppConfig config, string objectKey, Stream destination, CancellationToken cancellationToken = default)
    {
        return _transferService.DownloadFileAsync(config, objectKey, destination, cancellationToken);
    }

    public Task DownloadObjectToFileAsync(AppConfig config, string objectKey, string filePath, CancellationToken cancellationToken = default)
    {
        return _transferService.DownloadObjectToFileAsync(config, objectKey, filePath, cancellationToken);
    }

    public Task<IReadOnlyList<BucketItem>> ListAsync(AppConfig config, string? prefix = null, CancellationToken cancellationToken = default)
    {
        return _queryService.ListAsync(config, prefix, cancellationToken);
    }

    public Task<IReadOnlyList<BucketItem>> SearchAsync(AppConfig config, string term, CancellationToken cancellationToken = default)
    {
        return _queryService.SearchAsync(config, term, cancellationToken);
    }

    public Task<int> DeleteAsync(AppConfig config, BucketItem item, CancellationToken cancellationToken = default)
    {
        return _mutationService.DeleteAsync(config, item, cancellationToken);
    }

    public Task DeleteObjectByKeyAsync(AppConfig config, string objectKey, CancellationToken cancellationToken = default)
    {
        return _mutationService.DeleteObjectByKeyAsync(config, objectKey, cancellationToken);
    }

    public Task<string> CreateFolderAsync(AppConfig config, string folderName, string? prefix = null, CancellationToken cancellationToken = default)
    {
        return _mutationService.CreateFolderAsync(config, folderName, prefix, cancellationToken);
    }

    public Task<int> RenameAsync(AppConfig config, BucketItem item, string newDisplayName, CancellationToken cancellationToken = default)
    {
        return _mutationService.RenameAsync(config, item, newDisplayName, cancellationToken);
    }

    public Task<int> MoveAsync(AppConfig config, BucketItem item, string targetFolderPath, CancellationToken cancellationToken = default)
    {
        return _mutationService.MoveAsync(config, item, targetFolderPath, cancellationToken);
    }

    public Task<string> UploadFileAsync(AppConfig config, string filePath, string? prefix = null, string? relativeObjectPath = null, CancellationToken cancellationToken = default)
    {
        return _transferService.UploadFileAsync(config, filePath, prefix, relativeObjectPath, cancellationToken);
    }

    public Task UploadBytesAsync(AppConfig config, string objectKey, byte[] bytes, string? contentType = null, CancellationToken cancellationToken = default)
    {
        return _transferService.UploadBytesAsync(config, objectKey, bytes, contentType, cancellationToken);
    }

    public Task<byte[]> DownloadBytesAsync(AppConfig config, string objectKey, CancellationToken cancellationToken = default)
    {
        return _transferService.DownloadBytesAsync(config, objectKey, cancellationToken);
    }

    public Task<string> DownloadTextAsync(AppConfig config, string objectKey, CancellationToken cancellationToken = default)
    {
        return _transferService.DownloadTextAsync(config, objectKey, cancellationToken);
    }
}
