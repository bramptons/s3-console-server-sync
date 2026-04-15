using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using S3ConsoleSync.Models;
using System.Text;

namespace S3ConsoleSync.Services.Providers;

/// <summary>
/// Storage provider implementation for Azure Blob Storage.
/// </summary>
public class AzureBlobProvider : IStorageProvider
{
    internal const long LargeFileThresholdBytes = 8L * 1024 * 1024;
    internal const int BlockUploadChunkSizeBytes = 8 * 1024 * 1024;

    private readonly IAzureBlobClientFactory _clientFactory;

    public AzureBlobProvider(CredentialConfig credentials)
    {
        _clientFactory = new AzureBlobClientFactory(new BlobServiceClient(credentials.ConnectionString));
    }

    // Internal constructor for testing.
    internal AzureBlobProvider(BlobServiceClient serviceClient)
    {
        _clientFactory = new AzureBlobClientFactory(serviceClient);
    }

    internal AzureBlobProvider(IAzureBlobClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<IReadOnlyList<string>> ListObjectKeysAsync(
        string bucketOrContainer,
        string prefix,
        CancellationToken ct = default)
    {
        var containerClient = _clientFactory.GetBlobContainerClient(bucketOrContainer);
        var keys = new List<string>();

        await foreach (var item in containerClient.ListBlobsAsync(
            prefix: string.IsNullOrEmpty(prefix) ? null : prefix,
            cancellationToken: ct))
        {
            keys.Add(item.Name);
        }

        return keys;
    }

    public async Task<string> UploadFileAsync(
        string bucketOrContainer,
        string objectKey,
        string localFilePath,
        string storageTier,
        CancellationToken ct = default)
    {
        var containerClient = _clientFactory.GetBlobContainerClient(bucketOrContainer);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var accessTier = ResolveAccessTier(storageTier);
        var fileSize = new FileInfo(localFilePath).Length;

        if (fileSize >= LargeFileThresholdBytes)
        {
            return await UploadLargeFileAsync(containerClient, objectKey, localFilePath, accessTier, ct);
        }

        var blobClient = containerClient.GetBlobClient(objectKey);
        return await blobClient.UploadAsync(localFilePath, accessTier, ct);
    }

    public async Task DeleteObjectAsync(
        string bucketOrContainer,
        string objectKey,
        CancellationToken ct = default)
    {
        var containerClient = _clientFactory.GetBlobContainerClient(bucketOrContainer);
        var blobClient = containerClient.GetBlobClient(objectKey);
        await blobClient.DeleteIfExistsAsync(ct);
    }

    private static async Task<string> UploadLargeFileAsync(
        IAzureBlobContainerClient containerClient,
        string objectKey,
        string localFilePath,
        AccessTier? accessTier,
        CancellationToken ct)
    {
        var blockBlobClient = containerClient.GetBlockBlobClient(objectKey);
        var blockIds = new List<string>();
        var buffer = new byte[BlockUploadChunkSizeBytes];

        await using var stream = File.OpenRead(localFilePath);

        var blockNumber = 0;
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (bytesRead == 0)
                break;

            var blockId = BuildBlockId(blockNumber);
            await using var chunkStream = new MemoryStream(buffer, 0, bytesRead, writable: false);
            await blockBlobClient.StageBlockAsync(blockId, chunkStream, ct);

            blockIds.Add(blockId);
            blockNumber++;
        }

        return await blockBlobClient.CommitBlockListAsync(blockIds, accessTier, ct);
    }

    internal static string BuildBlockId(int blockNumber)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(blockNumber.ToString("D8")));
    }

    private static AccessTier? ResolveAccessTier(string tier) =>
        tier.ToUpperInvariant() switch
        {
            "HOT"     => AccessTier.Hot,
            "COOL"    => AccessTier.Cool,
            "ARCHIVE" => AccessTier.Archive,
            "COLD"    => AccessTier.Cold,
            _         => AccessTier.Hot
        };

    public void Dispose() { /* BlobServiceClient does not implement IDisposable */ }

    internal interface IAzureBlobClientFactory
    {
        IAzureBlobContainerClient GetBlobContainerClient(string bucketOrContainer);
    }

    internal interface IAzureBlobContainerClient
    {
        Task CreateIfNotExistsAsync(CancellationToken cancellationToken);
        IAsyncEnumerable<BlobItem> ListBlobsAsync(string? prefix, CancellationToken cancellationToken);
        IAzureBlobClient GetBlobClient(string objectKey);
        IAzureBlockBlobClient GetBlockBlobClient(string objectKey);
    }

    internal interface IAzureBlobClient
    {
        Task<string> UploadAsync(string localFilePath, AccessTier? accessTier, CancellationToken cancellationToken);
        Task DeleteIfExistsAsync(CancellationToken cancellationToken);
    }

    internal interface IAzureBlockBlobClient
    {
        Task StageBlockAsync(string blockId, Stream content, CancellationToken cancellationToken);
        Task<string> CommitBlockListAsync(IEnumerable<string> blockIds, AccessTier? accessTier, CancellationToken cancellationToken);
    }

    private sealed class AzureBlobClientFactory : IAzureBlobClientFactory
    {
        private readonly BlobServiceClient _serviceClient;

        public AzureBlobClientFactory(BlobServiceClient serviceClient)
        {
            _serviceClient = serviceClient;
        }

        public IAzureBlobContainerClient GetBlobContainerClient(string bucketOrContainer)
        {
            return new AzureBlobContainerClient(_serviceClient.GetBlobContainerClient(bucketOrContainer));
        }
    }

    private sealed class AzureBlobContainerClient : IAzureBlobContainerClient
    {
        private readonly BlobContainerClient _containerClient;

        public AzureBlobContainerClient(BlobContainerClient containerClient)
        {
            _containerClient = containerClient;
        }

        public async Task CreateIfNotExistsAsync(CancellationToken cancellationToken)
        {
            await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        public async IAsyncEnumerable<BlobItem> ListBlobsAsync(string? prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var item in _containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
            {
                yield return item;
            }
        }

        public IAzureBlobClient GetBlobClient(string objectKey)
        {
            return new AzureBlobClient(_containerClient.GetBlobClient(objectKey));
        }

        public IAzureBlockBlobClient GetBlockBlobClient(string objectKey)
        {
            return new AzureBlockBlobClient(_containerClient.GetBlockBlobClient(objectKey));
        }
    }

    private sealed class AzureBlobClient : IAzureBlobClient
    {
        private readonly BlobClient _blobClient;

        public AzureBlobClient(BlobClient blobClient)
        {
            _blobClient = blobClient;
        }

        public async Task<string> UploadAsync(string localFilePath, AccessTier? accessTier, CancellationToken cancellationToken)
        {
            await using var stream = File.OpenRead(localFilePath);
            var uploadOptions = new BlobUploadOptions
            {
                AccessTier = accessTier
            };
            var response = await _blobClient.UploadAsync(stream, uploadOptions, cancellationToken);
            return response.Value.ETag.ToString();
        }

        public async Task DeleteIfExistsAsync(CancellationToken cancellationToken)
        {
            await _blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
    }

    private sealed class AzureBlockBlobClient : IAzureBlockBlobClient
    {
        private readonly BlockBlobClient _blockBlobClient;

        public AzureBlockBlobClient(BlockBlobClient blockBlobClient)
        {
            _blockBlobClient = blockBlobClient;
        }

        public async Task StageBlockAsync(string blockId, Stream content, CancellationToken cancellationToken)
        {
            await _blockBlobClient.StageBlockAsync(blockId, content, cancellationToken: cancellationToken);
        }

        public async Task<string> CommitBlockListAsync(IEnumerable<string> blockIds, AccessTier? accessTier, CancellationToken cancellationToken)
        {
            var response = await _blockBlobClient.CommitBlockListAsync(blockIds, cancellationToken: cancellationToken);

            if (accessTier is not null)
                await _blockBlobClient.SetAccessTierAsync(accessTier.Value, cancellationToken: cancellationToken);

            return response.Value.ETag.ToString();
        }
    }
}
