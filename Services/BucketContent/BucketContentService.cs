using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DropAndForget.Models;
using DropAndForget.Services.Cloudflare;
using DropAndForget.Services.Encryption;
using DropAndForget.Services.Sync;

namespace DropAndForget.Services.BucketContent;

public sealed class BucketContentService(
    IR2BucketService bucketService,
    IEncryptedBucketService encryptedBucketService,
    ILocalSyncBrowser localSyncBrowser,
    IStorageModeCoordinator storageModeCoordinator)
{
    private readonly IR2BucketService _bucketService = bucketService;
    private readonly IEncryptedBucketService _encryptedBucketService = encryptedBucketService;
    private readonly ILocalSyncBrowser _localSyncBrowser = localSyncBrowser;
    private readonly IStorageModeCoordinator _storageModeCoordinator = storageModeCoordinator;

    private static bool UsesEncryptedBucket(AppConfig config)
    {
        return config.IsEncryptionEnabled;
    }

    private static bool UsesSyncFolder(AppConfig config)
    {
        return config.StorageMode == StorageMode.Sync;
    }

    public Task<IReadOnlyList<BucketItem>> ListAsync(AppConfig config, string prefix, CancellationToken cancellationToken = default)
    {
        if (UsesEncryptedBucket(config))
        {
            return R2UserFacingErrors.ExecuteAsync(() => _encryptedBucketService.ListAsync(config, prefix, cancellationToken), "Couldn't load bucket.");
        }

        if (UsesSyncFolder(config))
        {
            return Task.FromResult(_localSyncBrowser.ListItems(config, prefix));
        }

        return R2UserFacingErrors.ExecuteAsync(() => _bucketService.ListAsync(config, prefix, cancellationToken), "Couldn't load bucket.");
    }

    public Task<IReadOnlyList<BucketItem>> SearchAsync(AppConfig config, string term, CancellationToken cancellationToken = default)
    {
        if (UsesEncryptedBucket(config))
        {
            return R2UserFacingErrors.ExecuteAsync(() => _encryptedBucketService.SearchAsync(config, term, cancellationToken), "Couldn't search bucket.");
        }

        if (UsesSyncFolder(config))
        {
            return Task.FromResult(_localSyncBrowser.SearchItems(config, term));
        }

        return R2UserFacingErrors.ExecuteAsync(() => _bucketService.SearchAsync(config, term, cancellationToken), "Couldn't search bucket.");
    }

    public Task<string> CreateFolderAsync(AppConfig config, string folderName, string prefix, CancellationToken cancellationToken = default)
    {
        if (UsesEncryptedBucket(config))
        {
            return R2UserFacingErrors.ExecuteAsync(() => _encryptedBucketService.CreateFolderAsync(config, folderName, prefix, cancellationToken), "Couldn't create folder.");
        }

        if (UsesSyncFolder(config))
        {
            _localSyncBrowser.CreateFolder(config, prefix, folderName);
            return Task.FromResult(CombineObjectPath(prefix, folderName));
        }

        return R2UserFacingErrors.ExecuteAsync(() => _bucketService.CreateFolderAsync(config, folderName, prefix, cancellationToken), "Couldn't create folder.");
    }

    public Task<int> RenameAsync(AppConfig config, BucketItem item, string newName, CancellationToken cancellationToken = default)
    {
        if (UsesEncryptedBucket(config))
        {
            return R2UserFacingErrors.ExecuteAsync(() => _encryptedBucketService.RenameAsync(config, item, newName, cancellationToken), "Couldn't rename item.");
        }

        if (UsesSyncFolder(config))
        {
            _localSyncBrowser.RenameItem(config, item, newName);
            return Task.FromResult(1);
        }

        return R2UserFacingErrors.ExecuteAsync(() => _bucketService.RenameAsync(config, item, newName, cancellationToken), "Couldn't rename item.");
    }

    public Task<int> DeleteAsync(AppConfig config, BucketItem item, CancellationToken cancellationToken = default)
    {
        if (UsesEncryptedBucket(config))
        {
            return R2UserFacingErrors.ExecuteAsync(() => _encryptedBucketService.DeleteAsync(config, item, cancellationToken), "Couldn't delete item.");
        }

        if (UsesSyncFolder(config))
        {
            return Task.FromResult(_localSyncBrowser.DeleteItem(config, item));
        }

        return R2UserFacingErrors.ExecuteAsync(() => _bucketService.DeleteAsync(config, item, cancellationToken), "Couldn't delete item.");
    }

    public Task<int> MoveAsync(AppConfig config, BucketItem item, string targetFolderPath, CancellationToken cancellationToken = default)
    {
        if (UsesEncryptedBucket(config))
        {
            return R2UserFacingErrors.ExecuteAsync(() => _encryptedBucketService.MoveAsync(config, item, targetFolderPath, cancellationToken), "Couldn't move item.");
        }

        if (UsesSyncFolder(config))
        {
            _localSyncBrowser.MoveItem(config, item, targetFolderPath);
            return Task.FromResult(1);
        }

        return R2UserFacingErrors.ExecuteAsync(() => _bucketService.MoveAsync(config, item, targetFolderPath, cancellationToken), "Couldn't move item.");
    }

    public async Task<string> UploadFileAsync(AppConfig config, string filePath, string prefix, string relativeObjectPath, CancellationToken cancellationToken = default)
    {
        if (UsesEncryptedBucket(config))
        {
            return await R2UserFacingErrors.ExecuteAsync(() => _encryptedBucketService.UploadFileAsync(config, filePath, prefix, relativeObjectPath, cancellationToken), "Couldn't upload file.");
        }

        if (UsesSyncFolder(config))
        {
            await _storageModeCoordinator.CopyIntoSyncFolderAsync(config, [filePath], prefix, cancellationToken);
            return CombineObjectPath(prefix, relativeObjectPath);
        }

        return await R2UserFacingErrors.ExecuteAsync(() => _bucketService.UploadFileAsync(config, filePath, prefix, relativeObjectPath, cancellationToken), "Couldn't upload file.");
    }

    public Task DownloadFileAsync(AppConfig config, BucketItem item, Stream destination, CancellationToken cancellationToken = default)
    {
        if (UsesEncryptedBucket(config))
        {
            return R2UserFacingErrors.ExecuteAsync(() => _encryptedBucketService.DownloadFileAsync(config, item.Key, destination, cancellationToken), "Couldn't download file.");
        }

        if (UsesSyncFolder(config))
        {
            return _localSyncBrowser.CopyFileToAsync(config, item.Key, destination, cancellationToken);
        }

        return R2UserFacingErrors.ExecuteAsync(() => _bucketService.DownloadFileAsync(config, item.Key, destination, cancellationToken), "Couldn't download file.");
    }

    public Task DownloadFolderAsZipAsync(AppConfig config, BucketItem item, Stream destination, CancellationToken cancellationToken = default)
    {
        if (UsesEncryptedBucket(config))
        {
            return R2UserFacingErrors.ExecuteAsync(() => _encryptedBucketService.DownloadFolderAsZipAsync(config, item, destination, cancellationToken), "Couldn't download folder.");
        }

        if (UsesSyncFolder(config))
        {
            return _storageModeCoordinator.DownloadLocalFolderAsZipAsync(config, item.Key, destination, cancellationToken);
        }

        return R2UserFacingErrors.ExecuteAsync(() => _bucketService.DownloadFolderAsZipAsync(config, item, destination, cancellationToken), "Couldn't download folder.");
    }

    public Task<byte[]> DownloadBytesAsync(AppConfig config, BucketItem item, CancellationToken cancellationToken = default)
    {
        if (UsesEncryptedBucket(config))
        {
            return R2UserFacingErrors.ExecuteAsync(() => _encryptedBucketService.DownloadBytesAsync(config, item.Key, cancellationToken), "Couldn't load file content.");
        }

        if (UsesSyncFolder(config))
        {
            return _localSyncBrowser.ReadAllBytesAsync(config, item.Key, cancellationToken);
        }

        return R2UserFacingErrors.ExecuteAsync(() => _bucketService.DownloadBytesAsync(config, item.Key, cancellationToken), "Couldn't load file content.");
    }

    private static string CombineObjectPath(string prefix, string relativePath)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return relativePath.TrimStart('/');
        }

        return prefix.TrimEnd('/') + "/" + relativePath.TrimStart('/');
    }
}
