using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using S3ConsoleSync.Models;
using Serilog;

namespace S3ConsoleSync.Services.Providers;

/// <summary>
/// Storage provider implementation for AWS S3.
/// </summary>
public class AwsS3Provider : IStorageProvider, IDisposable
{
    internal const long MultipartUploadThresholdBytes = 8L * 1024 * 1024;
    internal const long MultipartUploadPartSizeBytes = 8L * 1024 * 1024;

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
        var fileSize = new FileInfo(localFilePath).Length;

        if (fileSize >= MultipartUploadThresholdBytes)
        {
            return await UploadMultipartAsync(
                bucketOrContainer,
                objectKey,
                localFilePath,
                storageClass,
                fileSize,
                ct);
        }

        var uploadRequest = new PutObjectRequest
        {
            BucketName = bucketOrContainer,
            Key = objectKey,
            FilePath = localFilePath,
            StorageClass = storageClass
        };

        var response = await _client.PutObjectAsync(uploadRequest, ct);
        return response.ETag ?? string.Empty;
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

    private async Task<string> UploadMultipartAsync(
        string bucketOrContainer,
        string objectKey,
        string localFilePath,
        S3StorageClass storageClass,
        long fileSize,
        CancellationToken ct)
    {
        var createResponse = await _client.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest
            {
                BucketName = bucketOrContainer,
                Key = objectKey,
                StorageClass = storageClass
            },
            ct);

        var partETags = new List<PartETag>();

        try
        {
            long filePosition = 0;
            var partNumber = 1;

            while (filePosition < fileSize)
            {
                var partSize = Math.Min(MultipartUploadPartSizeBytes, fileSize - filePosition);
                var uploadResponse = await _client.UploadPartAsync(
                    new UploadPartRequest
                    {
                        BucketName = bucketOrContainer,
                        Key = objectKey,
                        UploadId = createResponse.UploadId,
                        PartNumber = partNumber,
                        FilePath = localFilePath,
                        FilePosition = filePosition,
                        PartSize = partSize
                    },
                    ct);

                partETags.Add(new PartETag(partNumber, uploadResponse.ETag));
                filePosition += partSize;
                partNumber++;
            }

            var completeResponse = await _client.CompleteMultipartUploadAsync(
                new CompleteMultipartUploadRequest
                {
                    BucketName = bucketOrContainer,
                    Key = objectKey,
                    UploadId = createResponse.UploadId,
                    PartETags = partETags
                },
                ct);

            return completeResponse.ETag ?? string.Empty;
        }
        catch
        {
            try
            {
                await _client.AbortMultipartUploadAsync(
                    new AbortMultipartUploadRequest
                    {
                        BucketName = bucketOrContainer,
                        Key = objectKey,
                        UploadId = createResponse.UploadId
                    },
                    ct);
            }
            catch (Exception abortEx)
            {
                Log.Warning(abortEx, "Failed to abort multipart upload for '{Key}'", objectKey);
            }

            throw;
        }
    }

    public void Dispose() => _client.Dispose();
}
