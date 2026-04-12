namespace S3ConsoleSync.Services.Providers;

/// <summary>
/// Abstraction over a cloud storage back-end.
/// </summary>
public interface IStorageProvider : IDisposable
{
    /// <summary>
    /// List all object keys currently stored in the target bucket/container
    /// under the given prefix.
    /// </summary>
    Task<IReadOnlyList<string>> ListObjectKeysAsync(string bucketOrContainer, string prefix, CancellationToken ct = default);

    /// <summary>
    /// Upload a local file to the storage back-end.
    /// </summary>
    /// <returns>The ETag of the newly uploaded object.</returns>
    Task<string> UploadFileAsync(
        string bucketOrContainer,
        string objectKey,
        string localFilePath,
        string storageTier,
        CancellationToken ct = default);

    /// <summary>
    /// Delete a single object from the storage back-end.
    /// </summary>
    Task DeleteObjectAsync(string bucketOrContainer, string objectKey, CancellationToken ct = default);
}
