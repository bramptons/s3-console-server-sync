namespace S3ConsoleSync.Models;

/// <summary>
/// Persisted state for a single sync job, used to detect changed and deleted
/// files between runs without re-hashing every object in the cloud.
/// </summary>
public class SyncState
{
    /// <summary>UTC timestamp of the last successful sync run.</summary>
    public DateTime LastSyncUtc { get; set; } = DateTime.MinValue;

    /// <summary>
    /// Map of relative file path → file metadata captured at the time of the
    /// last upload.  Relative paths use forward slashes regardless of OS.
    /// </summary>
    public Dictionary<string, FileState> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Metadata captured for a single file at the time it was last synced.
/// </summary>
public class FileState
{
    /// <summary>MD5 hex hash of the file contents.</summary>
    public string ContentMd5 { get; set; } = string.Empty;

    /// <summary>File size in bytes at last upload.</summary>
    public long SizeBytes { get; set; }

    /// <summary>UTC last-modified timestamp from the local file system.</summary>
    public DateTime LastModifiedUtc { get; set; }

    /// <summary>ETag returned by the cloud provider after the upload.</summary>
    public string ETag { get; set; } = string.Empty;
}
