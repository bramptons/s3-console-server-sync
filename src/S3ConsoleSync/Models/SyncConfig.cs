using System.Text.Json.Serialization;

namespace S3ConsoleSync.Models;

/// <summary>
/// Configuration for a single sync job, stored as a flat JSON file.
/// </summary>
public class SyncConfig
{
    /// <summary>Friendly name for this sync job.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Local folder or drive root to sync (e.g. "C:\Backups" or "D:\").</summary>
    public string SourceFolder { get; set; } = string.Empty;

    /// <summary>The cloud storage provider to use.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StorageProviderType Provider { get; set; } = StorageProviderType.S3;

    /// <summary>S3 bucket name or Azure Blob container name.</summary>
    public string BucketOrContainer { get; set; } = string.Empty;

    /// <summary>
    /// Optional key prefix / "virtual folder" inside the bucket or container.
    /// Leave empty to store files at the root.
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>AWS region (e.g. "us-east-1"). Not used for Azure.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>Provider-specific credential block.</summary>
    public CredentialConfig Credentials { get; set; } = new();

    /// <summary>
    /// When true, files that no longer exist locally will be removed from the
    /// cloud storage during sync.
    /// </summary>
    public bool DeleteRemovedFiles { get; set; } = false;

    /// <summary>
    /// Storage class / tier for newly uploaded objects.
    /// AWS S3 / Wasabi values: STANDARD, STANDARD_IA, ONEZONE_IA, INTELLIGENT_TIERING, GLACIER, DEEP_ARCHIVE.
    /// Azure values: Hot, Cool, Archive.
    /// </summary>
    public string StorageTier { get; set; } = "STANDARD";

    /// <summary>
    /// File patterns to exclude from the sync (e.g. "*.tmp", "Thumbs.db").
    /// Standard shell glob syntax is supported.
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new();

    /// <summary>
    /// Path to the state file used for change-detection between runs.
    /// Defaults to "&lt;SourceFolder&gt;/.s3sync_state.json" when empty.
    /// </summary>
    public string StateFilePath { get; set; } = string.Empty;
}

/// <summary>
/// Credentials for the chosen storage provider.
/// </summary>
public class CredentialConfig
{
    // AWS S3 / Wasabi
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    // Azure Blob Storage
    public string ConnectionString { get; set; } = string.Empty;

    // Wasabi uses a custom endpoint; leave empty to default to the Wasabi us-east-1 endpoint.
    public string CustomEndpoint { get; set; } = string.Empty;
}
