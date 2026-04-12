using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using S3ConsoleSync.Models;

namespace S3ConsoleSync.Services.Providers;

/// <summary>
/// Storage provider implementation for Azure Blob Storage.
/// </summary>
public class AzureBlobProvider : IStorageProvider
{
    private readonly BlobServiceClient _serviceClient;

    public AzureBlobProvider(CredentialConfig credentials)
    {
        _serviceClient = new BlobServiceClient(credentials.ConnectionString);
    }

    // Internal constructor for testing.
    internal AzureBlobProvider(BlobServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }
    public async Task<IReadOnlyList<string>> ListObjectKeysAsync(
        string bucketOrContainer,
        string prefix,
        CancellationToken ct = default)
    {
        var containerClient = _serviceClient.GetBlobContainerClient(bucketOrContainer);
        var keys = new List<string>();

        await foreach (var item in containerClient.GetBlobsAsync(
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
        var containerClient = _serviceClient.GetBlobContainerClient(bucketOrContainer);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var blobClient = containerClient.GetBlobClient(objectKey);

        var accessTier = ResolveAccessTier(storageTier);

        await using var stream = File.OpenRead(localFilePath);
        var uploadOptions = new BlobUploadOptions
        {
            AccessTier = accessTier
        };
        var response = await blobClient.UploadAsync(stream, uploadOptions, ct);
        return response.Value.ETag.ToString();
    }

    public async Task DeleteObjectAsync(
        string bucketOrContainer,
        string objectKey,
        CancellationToken ct = default)
    {
        var containerClient = _serviceClient.GetBlobContainerClient(bucketOrContainer);
        var blobClient = containerClient.GetBlobClient(objectKey);
        await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
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
}
