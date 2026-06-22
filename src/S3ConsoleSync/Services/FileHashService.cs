using System.Security.Cryptography;

namespace S3ConsoleSync.Services;

/// <summary>
/// Computes MD5 content hashes for local files, used to detect changes
/// between sync runs without reading back from cloud storage.
/// </summary>
public class FileHashService
{
    /// <summary>
    /// Compute a hex-encoded MD5 hash for the contents of <paramref name="filePath"/>.
    /// </summary>
    public virtual string ComputeMd5(string filePath)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        var hash = md5.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Returns true when the local file differs from the persisted state.
    /// Size is used as a cheap fast-path, then MD5 decides same-sized files so
    /// metadata-only timestamp changes do not force media files to re-upload.
    /// </summary>
    public virtual bool HasChanged(string filePath, Models.FileState? previous)
    {
        if (previous is null)
            return true;

        var info = new FileInfo(filePath);
        if (!info.Exists)
            return false; // caller should handle missing files separately

        if (info.Length != previous.SizeBytes)
            return true;

        // Full hash check decides content equality. Last-modified timestamps
        // are stored for diagnostics/state, but are not used to force uploads.
        return ComputeMd5(filePath) != previous.ContentMd5;
    }
}
