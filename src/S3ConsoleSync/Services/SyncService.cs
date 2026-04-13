using S3ConsoleSync.Models;
using S3ConsoleSync.Services.Providers;
using Serilog;

namespace S3ConsoleSync.Services;

/// <summary>
/// Executes a single sync job: enumerate local files, diff against persisted
/// state, upload changed / new files, optionally delete removed files, then
/// persist the updated state.
/// </summary>
public class SyncService
{
    private readonly ConfigService _configService;
    private readonly FileHashService _hashService;

    public SyncService(ConfigService configService, FileHashService hashService)
    {
        _configService = configService;
        _hashService = hashService;
    }

    /// <summary>
    /// Run the sync described by <paramref name="config"/>, using
    /// <paramref name="provider"/> as the cloud back-end.
    /// </summary>
    public async Task<SyncResult> RunAsync(
        SyncConfig config,
        IStorageProvider provider,
        CancellationToken ct = default)
    {
        var result = new SyncResult
        {
            ConfigName = config.Name,
            StartedUtc = DateTime.UtcNow
        };

        Log.Information("Starting sync job '{Name}': {Source} → {Provider}/{Bucket}",
            config.Name, config.SourceFolder, config.Provider, config.BucketOrContainer);

        try
        {
            var state = _configService.LoadState(config);

            // 1. Enumerate local files.
            var localFiles = EnumerateLocalFiles(config);

            // 2. Build a set of relative keys that represent the current local tree.
            var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var localPath in localFiles)
            {
                ct.ThrowIfCancellationRequested();

                var relativeKey = BuildObjectKey(config, localPath);
                currentKeys.Add(relativeKey);

                // Diff check.
                state.Files.TryGetValue(relativeKey, out var previousState);
                if (!_hashService.HasChanged(localPath, previousState))
                {
                    Log.Debug("Skipping unchanged file: {File}", relativeKey);
                    result.FilesSkipped++;
                    continue;
                }

                // Upload.
                Log.Information("Uploading: {Key}", relativeKey);
                var fileInfo = new FileInfo(localPath);
                var md5 = _hashService.ComputeMd5(localPath);

                try
                {
                    var etag = await provider.UploadFileAsync(
                        config.BucketOrContainer,
                        relativeKey,
                        localPath,
                        config.StorageTier,
                        ct);

                    state.Files[relativeKey] = new FileState
                    {
                        ContentMd5 = md5,
                        SizeBytes = fileInfo.Length,
                        LastModifiedUtc = fileInfo.LastWriteTimeUtc,
                        ETag = etag
                    };

                    result.FilesUploaded++;
                    result.BytesUploaded += fileInfo.Length;
                    Log.Information("Uploaded: {Key} ({Size:N0} bytes)", relativeKey, fileInfo.Length);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Log.Error(ex, "Failed to upload {Key}", relativeKey);
                }
            }

            // 3. Handle deletions.
            if (config.DeleteRemovedFiles)
            {
                var remoteKeys = await provider.ListObjectKeysAsync(
                    config.BucketOrContainer,
                    config.KeyPrefix,
                    ct);

                foreach (var remoteKey in remoteKeys)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!currentKeys.Contains(remoteKey))
                    {
                        Log.Information("Deleting removed file from storage: {Key}", remoteKey);
                        try
                        {
                            await provider.DeleteObjectAsync(config.BucketOrContainer, remoteKey, ct);
                            state.Files.Remove(remoteKey);
                            result.FilesDeleted++;
                        }
                        catch (Exception ex) when (!ct.IsCancellationRequested)
                        {
                            Log.Error(ex, "Failed to delete remote key {Key}", remoteKey);
                        }
                    }
                }
            }

            // 4. Persist updated state.
            state.LastSyncUtc = DateTime.UtcNow;
            _configService.SaveState(config, state);

            result.Success = true;
            Log.Information(
                "Sync '{Name}' complete. Uploaded: {Up}, Skipped: {Skip}, Deleted: {Del}, Bytes: {Bytes:N0}",
                config.Name, result.FilesUploaded, result.FilesSkipped, result.FilesDeleted, result.BytesUploaded);
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Sync was cancelled.";
            Log.Warning("Sync '{Name}' was cancelled.", config.Name);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            Log.Error(ex, "Sync '{Name}' failed.", config.Name);
        }

        result.FinishedUtc = DateTime.UtcNow;
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<string> EnumerateLocalFiles(SyncConfig config)
    {
        if (!Directory.Exists(config.SourceFolder))
        {
            Log.Warning("Source folder does not exist: {Folder}", config.SourceFolder);
            yield break;
        }

        // Resolve the state file path so we can exclude it from the sync.
        var stateFilePath = Path.GetFullPath(ConfigService.ResolveStatePath(config));

        foreach (var file in Directory.EnumerateFiles(config.SourceFolder, "*", SearchOption.AllDirectories))
        {
            // Never sync the state file itself.
            if (string.Equals(Path.GetFullPath(file), stateFilePath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsExcluded(file, config.ExcludePatterns))
            {
                Log.Debug("Excluding: {File}", file);
                continue;
            }

            yield return file;
        }
    }

    private static string BuildObjectKey(SyncConfig config, string localFilePath)
    {
        // Make the path relative to the source folder and normalise to forward slashes.
        var relative = Path.GetRelativePath(config.SourceFolder, localFilePath)
                           .Replace('\\', '/');

        return string.IsNullOrEmpty(config.KeyPrefix)
            ? relative
            : $"{config.KeyPrefix.TrimEnd('/')}/{relative}";
    }

    private static bool IsExcluded(string filePath, IEnumerable<string> patterns)
    {
        var fileName = Path.GetFileName(filePath);
        foreach (var pattern in patterns)
        {
            if (GlobMatch(pattern, fileName) || GlobMatch(pattern, filePath))
                return true;
        }
        return false;
    }

    /// <summary>Simple glob matcher supporting * and ? wildcards.</summary>
    private static bool GlobMatch(string pattern, string input)
    {
        // Convert the glob pattern to a regex and test the input.
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(
            input,
            regexPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
