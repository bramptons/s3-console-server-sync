using System.Text.Json;
using S3ConsoleSync.Models;

namespace S3ConsoleSync.Services;

/// <summary>
/// Loads and saves <see cref="SyncConfig"/> and <see cref="SyncState"/> flat JSON files.
/// </summary>
public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    // ── Config ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Load a <see cref="SyncConfig"/> from a JSON file at <paramref name="path"/>.
    /// </summary>
    public virtual SyncConfig LoadConfig(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SyncConfig>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialise config at '{path}'.");
    }

    /// <summary>
    /// Load all <c>*.json</c> config files found in <paramref name="configDirectory"/>.
    /// </summary>
    public virtual IReadOnlyList<SyncConfig> LoadAllConfigs(string configDirectory)
    {
        if (!Directory.Exists(configDirectory))
            return Array.Empty<SyncConfig>();

        return Directory.GetFiles(configDirectory, "*.json", SearchOption.TopDirectoryOnly)
                        .Select(LoadConfig)
                        .ToList();
    }

    /// <summary>
    /// Persist a <see cref="SyncConfig"/> to <paramref name="path"/> as formatted JSON.
    /// </summary>
    public virtual void SaveConfig(SyncConfig config, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
    }

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Return the resolved state-file path for a given config.
    /// </summary>
    public static string ResolveStatePath(SyncConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.StateFilePath))
            return config.StateFilePath;

        var safeName = string.Concat(config.Name.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "S3ConsoleSync",
            $"{safeName}.state.json");
    }

    /// <summary>Load the persisted <see cref="SyncState"/> for a config, or return a fresh one.</summary>
    public virtual SyncState LoadState(SyncConfig config)
    {
        var path = ResolveStatePath(config);
        if (!File.Exists(path))
            return new SyncState();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SyncState>(json, JsonOptions) ?? new SyncState();
        }
        catch
        {
            return new SyncState();
        }
    }

    /// <summary>Persist the <see cref="SyncState"/> for a config.</summary>
    public virtual void SaveState(SyncConfig config, SyncState state)
    {
        var path = ResolveStatePath(config);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
    }
}
