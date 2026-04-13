using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using S3ConsoleSync.Models;

namespace S3ConsoleSync.Services.Providers;

/// <summary>
/// Storage provider implementation for Wasabi Hot Storage.
/// Wasabi exposes an S3-compatible API, so we reuse <see cref="AwsS3Provider"/>
/// but override the service endpoint.
///
/// Default regional endpoints (set <see cref="CredentialConfig.CustomEndpoint"/>
/// to override):
///   us-east-1  → s3.wasabisys.com
///   us-east-2  → s3.us-east-2.wasabisys.com
///   us-west-1  → s3.us-west-1.wasabisys.com
///   eu-central-1 → s3.eu-central-1.wasabisys.com
///   ap-northeast-1 → s3.ap-northeast-1.wasabisys.com
/// </summary>
public class WasabiProvider : IStorageProvider, IDisposable
{
    private readonly AwsS3Provider _inner;

    private static readonly IReadOnlyDictionary<string, string> RegionEndpoints =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["us-east-1"]      = "https://s3.wasabisys.com",
            ["us-east-2"]      = "https://s3.us-east-2.wasabisys.com",
            ["us-west-1"]      = "https://s3.us-west-1.wasabisys.com",
            ["eu-central-1"]   = "https://s3.eu-central-1.wasabisys.com",
            ["ap-northeast-1"] = "https://s3.ap-northeast-1.wasabisys.com"
        };

    public WasabiProvider(CredentialConfig credentials, string region)
    {
        var endpoint = string.IsNullOrWhiteSpace(credentials.CustomEndpoint)
            ? (RegionEndpoints.TryGetValue(region, out var ep) ? ep : "https://s3.wasabisys.com")
            : credentials.CustomEndpoint;

        var awsCredentials = new BasicAWSCredentials(credentials.AccessKey, credentials.SecretKey);
        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true
        };
        var client = new AmazonS3Client(awsCredentials, config);
        _inner = new AwsS3Provider(client);
    }

    public Task<IReadOnlyList<string>> ListObjectKeysAsync(string bucketOrContainer, string prefix, CancellationToken ct = default)
        => _inner.ListObjectKeysAsync(bucketOrContainer, prefix, ct);

    public Task<string> UploadFileAsync(string bucketOrContainer, string objectKey, string localFilePath, string storageTier, CancellationToken ct = default)
        => _inner.UploadFileAsync(bucketOrContainer, objectKey, localFilePath, storageTier, ct);

    public Task DeleteObjectAsync(string bucketOrContainer, string objectKey, CancellationToken ct = default)
        => _inner.DeleteObjectAsync(bucketOrContainer, objectKey, ct);

    public void Dispose() => _inner.Dispose();
}
