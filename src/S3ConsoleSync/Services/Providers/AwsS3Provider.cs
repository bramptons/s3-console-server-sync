using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using S3ConsoleSync.Models;

namespace S3ConsoleSync.Services.Providers;

/// <summary>
/// Storage provider implementation for AWS S3.
/// </summary>
public class AwsS3Provider : IStorageProvider, IDisposable
{
    protected readonly IAmazonS3 _client;

    public AwsS3Provider(CredentialConfig credentials, string region)
    {
        var awsCredentials = new BasicAWSCredentials(credentials.AccessKey, credentials.SecretKey);
        var regionEndpoint = RegionEndpoint.GetBySystemName(region);
        _client = new AmazonS3Client(awsCredentials, regionEndpoint);
    }

    // Internal constructor for testing / subclasses that supply their own client.
    internal AwsS3Provider(IAmazonS3 client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<string>> ListObjectKeysAsync(
        string bucketOrContainer,
        string prefix,
        CancellationToken ct = default)
    {
        var keys = new List<string>();
        string? continuationToken = null;

        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = bucketOrContainer,
                Prefix = string.IsNullOrEmpty(prefix) ? null : prefix,
                ContinuationToken = continuationToken
            };

            var response = await _client.ListObjectsV2Async(request, ct);
            keys.AddRange(response.S3Objects.Select(o => o.Key));
            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (continuationToken != null);

        return keys;
    }

    public async Task<string> UploadFileAsync(
        string bucketOrContainer,
        string objectKey,
        string localFilePath,
        string storageTier,
        CancellationToken ct = default)
    {
        var storageClass = ResolveStorageClass(storageTier);

        using var transferUtility = new TransferUtility(_client);
        var uploadRequest = new TransferUtilityUploadRequest
        {
            BucketName = bucketOrContainer,
            Key = objectKey,
            FilePath = localFilePath,
            StorageClass = storageClass
        };

        await transferUtility.UploadAsync(uploadRequest, ct);

        // Retrieve the ETag of the uploaded object.
        var meta = await _client.GetObjectMetadataAsync(bucketOrContainer, objectKey, ct);
        return meta.ETag ?? string.Empty;
    }

    public async Task DeleteObjectAsync(
        string bucketOrContainer,
        string objectKey,
        CancellationToken ct = default)
    {
        await _client.DeleteObjectAsync(bucketOrContainer, objectKey, ct);
    }

    protected virtual S3StorageClass ResolveStorageClass(string tier) =>
        tier.ToUpperInvariant() switch
        {
            "STANDARD_IA"          => S3StorageClass.StandardInfrequentAccess,
            "ONEZONE_IA"           => S3StorageClass.OneZoneInfrequentAccess,
            "INTELLIGENT_TIERING"  => S3StorageClass.IntelligentTiering,
            "GLACIER"              => S3StorageClass.Glacier,
            "GLACIER_IR"           => S3StorageClass.GlacierInstantRetrieval,
            "DEEP_ARCHIVE"         => S3StorageClass.DeepArchive,
            _                      => S3StorageClass.Standard
        };

    public void Dispose() => _client.Dispose();
}
