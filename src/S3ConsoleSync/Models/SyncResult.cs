namespace S3ConsoleSync.Models;

/// <summary>
/// Summary produced by a sync run — returned by <see cref="Services.SyncService"/>.
/// </summary>
public class SyncResult
{
    public string ConfigName { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public DateTime FinishedUtc { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public int FilesUploaded { get; set; }
    public int FilesSkipped { get; set; }
    public int FilesDeleted { get; set; }
    public long BytesUploaded { get; set; }
}
