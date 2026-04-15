using Azure.Storage.Blobs.Models;
using S3ConsoleSync.Services.Providers;

namespace S3ConsoleSync.Tests;

public class AzureBlobProviderTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public async Task UploadFileAsync_UsesSimpleUpload_ForSmallFiles()
    {
        var blobClient = new FakeBlobClient { UploadResult = "small-etag" };
        var blockBlobClient = new FakeBlockBlobClient();
        var containerClient = new FakeContainerClient(blobClient, blockBlobClient);
        var provider = new AzureBlobProvider(new FakeClientFactory(containerClient));
        var filePath = CreateTempFile(AzureBlobProvider.LargeFileThresholdBytes - 1);

        var etag = await provider.UploadFileAsync("container", "object-key", filePath, "COOL");

        Assert.Equal("small-etag", etag);
        Assert.Equal(filePath, blobClient.UploadedFilePath);
        Assert.Equal(AccessTier.Cool, blobClient.UploadedAccessTier);
        Assert.Empty(blockBlobClient.StagedBlocks);
    }

    [Fact]
    public async Task UploadFileAsync_UsesBlockUpload_ForLargeFiles()
    {
        var blobClient = new FakeBlobClient();
        var blockBlobClient = new FakeBlockBlobClient { CommitResult = "large-etag" };
        var containerClient = new FakeContainerClient(blobClient, blockBlobClient);
        var provider = new AzureBlobProvider(new FakeClientFactory(containerClient));
        var filePath = CreateTempFile(AzureBlobProvider.LargeFileThresholdBytes + 512);

        var etag = await provider.UploadFileAsync("container", "object-key", filePath, "ARCHIVE");

        Assert.Equal("large-etag", etag);
        Assert.Null(blobClient.UploadedFilePath);
        Assert.Equal(2, blockBlobClient.StagedBlocks.Count);
        Assert.Equal(AzureBlobProvider.BlockUploadChunkSizeBytes, blockBlobClient.StagedBlocks[0].Length);
        Assert.Equal(512, blockBlobClient.StagedBlocks[1].Length);
        Assert.Equal(AccessTier.Archive, blockBlobClient.CommittedAccessTier);
        Assert.Equal(
            new[] { AzureBlobProvider.BuildBlockId(0), AzureBlobProvider.BuildBlockId(1) },
            blockBlobClient.CommittedBlockIds);
    }

    public void Dispose()
    {
        foreach (var tempFile in _tempFiles)
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private string CreateTempFile(long length)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"azureblob-{Guid.NewGuid():N}.tmp");
        using var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
        _tempFiles.Add(filePath);
        return filePath;
    }

    private sealed class FakeClientFactory : AzureBlobProvider.IAzureBlobClientFactory
    {
        private readonly AzureBlobProvider.IAzureBlobContainerClient _containerClient;

        public FakeClientFactory(AzureBlobProvider.IAzureBlobContainerClient containerClient)
        {
            _containerClient = containerClient;
        }

        public AzureBlobProvider.IAzureBlobContainerClient GetBlobContainerClient(string bucketOrContainer)
        {
            return _containerClient;
        }
    }

    private sealed class FakeContainerClient : AzureBlobProvider.IAzureBlobContainerClient
    {
        private readonly AzureBlobProvider.IAzureBlobClient _blobClient;
        private readonly AzureBlobProvider.IAzureBlockBlobClient _blockBlobClient;

        public FakeContainerClient(
            AzureBlobProvider.IAzureBlobClient blobClient,
            AzureBlobProvider.IAzureBlockBlobClient blockBlobClient)
        {
            _blobClient = blobClient;
            _blockBlobClient = blockBlobClient;
        }

        public Task CreateIfNotExistsAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<BlobItem> ListBlobsAsync(string? prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public AzureBlobProvider.IAzureBlobClient GetBlobClient(string objectKey)
        {
            return _blobClient;
        }

        public AzureBlobProvider.IAzureBlockBlobClient GetBlockBlobClient(string objectKey)
        {
            return _blockBlobClient;
        }
    }

    private sealed class FakeBlobClient : AzureBlobProvider.IAzureBlobClient
    {
        public string? UploadedFilePath { get; private set; }
        public AccessTier? UploadedAccessTier { get; private set; }
        public string UploadResult { get; init; } = "etag";

        public Task<string> UploadAsync(string localFilePath, AccessTier? accessTier, CancellationToken cancellationToken)
        {
            UploadedFilePath = localFilePath;
            UploadedAccessTier = accessTier;
            return Task.FromResult(UploadResult);
        }

        public Task DeleteIfExistsAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBlockBlobClient : AzureBlobProvider.IAzureBlockBlobClient
    {
        public List<(string BlockId, int Length)> StagedBlocks { get; } = new();
        public IReadOnlyList<string> CommittedBlockIds { get; private set; } = Array.Empty<string>();
        public AccessTier? CommittedAccessTier { get; private set; }
        public string CommitResult { get; init; } = "etag";

        public async Task StageBlockAsync(string blockId, Stream content, CancellationToken cancellationToken)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            StagedBlocks.Add((blockId, checked((int)copy.Length)));
        }

        public Task<string> CommitBlockListAsync(IEnumerable<string> blockIds, AccessTier? accessTier, CancellationToken cancellationToken)
        {
            CommittedBlockIds = blockIds.ToArray();
            CommittedAccessTier = accessTier;
            return Task.FromResult(CommitResult);
        }
    }
}