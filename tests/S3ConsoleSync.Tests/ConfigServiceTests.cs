using System.Text.Json;
using S3ConsoleSync.Models;
using S3ConsoleSync.Services;

namespace S3ConsoleSync.Tests;

public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _sut;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"s3sync_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _sut = new ConfigService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void SaveConfig_ThenLoadConfig_RoundTrips()
    {
        var expected = new SyncConfig
        {
            Name = "TestJob",
            SourceFolder = @"C:\Data",
            Provider = StorageProviderType.Wasabi,
            BucketOrContainer = "my-bucket",
            Region = "eu-central-1",
            StorageTier = "STANDARD_IA",
            DeleteRemovedFiles = true,
            KeyPrefix = "server1",
            ExcludePatterns = new List<string> { "*.tmp", "Thumbs.db" },
            Credentials = new CredentialConfig { AccessKey = "AK", SecretKey = "SK" }
        };

        var path = Path.Combine(_tempDir, "testjob.json");
        _sut.SaveConfig(expected, path);

        var actual = _sut.LoadConfig(path);

        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.SourceFolder, actual.SourceFolder);
        Assert.Equal(StorageProviderType.Wasabi, actual.Provider);
        Assert.Equal(expected.BucketOrContainer, actual.BucketOrContainer);
        Assert.Equal(expected.Region, actual.Region);
        Assert.Equal(expected.StorageTier, actual.StorageTier);
        Assert.True(actual.DeleteRemovedFiles);
        Assert.Equal(expected.KeyPrefix, actual.KeyPrefix);
        Assert.Equal(2, actual.ExcludePatterns.Count);
        Assert.Equal("AK", actual.Credentials.AccessKey);
    }

    [Fact]
    public void LoadAllConfigs_ReturnsAllJsonFilesInDirectory()
    {
        // Arrange: write two config files and one non-json file.
        for (var i = 1; i <= 2; i++)
        {
            var cfg = new SyncConfig { Name = $"Job{i}", SourceFolder = $@"C:\Data{i}" };
            _sut.SaveConfig(cfg, Path.Combine(_tempDir, $"job{i}.json"));
        }
        File.WriteAllText(Path.Combine(_tempDir, "notes.txt"), "ignore me");

        var configs = _sut.LoadAllConfigs(_tempDir);

        Assert.Equal(2, configs.Count);
    }

    [Fact]
    public void LoadAllConfigs_EmptyDirectory_ReturnsEmpty()
    {
        Assert.Empty(_sut.LoadAllConfigs(_tempDir));
    }

    [Fact]
    public void LoadAllConfigs_NonExistentDirectory_ReturnsEmpty()
    {
        Assert.Empty(_sut.LoadAllConfigs(Path.Combine(_tempDir, "nonexistent")));
    }

    [Fact]
    public void SaveState_ThenLoadState_RoundTrips()
    {
        var config = new SyncConfig
        {
            Name = "StateJob",
            StateFilePath = Path.Combine(_tempDir, "state.json")
        };

        var state = new SyncState
        {
            LastSyncUtc = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            Files =
            {
                ["folder/file.txt"] = new FileState
                {
                    ContentMd5 = "abc123",
                    SizeBytes = 1024,
                    LastModifiedUtc = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                    ETag = "\"etag1\""
                }
            }
        };

        _sut.SaveState(config, state);
        var loaded = _sut.LoadState(config);

        Assert.Equal(state.LastSyncUtc, loaded.LastSyncUtc);
        Assert.True(loaded.Files.ContainsKey("folder/file.txt"));
        Assert.Equal("abc123", loaded.Files["folder/file.txt"].ContentMd5);
        Assert.Equal(1024, loaded.Files["folder/file.txt"].SizeBytes);
    }

    [Fact]
    public void LoadState_MissingStateFile_ReturnsFreshState()
    {
        var config = new SyncConfig
        {
            Name = "NoState",
            StateFilePath = Path.Combine(_tempDir, "missing.json")
        };

        var state = _sut.LoadState(config);

        Assert.Empty(state.Files);
        Assert.Equal(DateTime.MinValue, state.LastSyncUtc);
    }

    [Fact]
    public void ResolveStatePath_ExplicitPath_UsesExplicitPath()
    {
        var explicit_ = @"/tmp/custom-state.json";
        var config = new SyncConfig { Name = "X", StateFilePath = explicit_ };
        Assert.Equal(explicit_, ConfigService.ResolveStatePath(config));
    }

    [Fact]
    public void ResolveStatePath_NoExplicitPath_UsesLocalAppData()
    {
        var config = new SyncConfig { Name = "MyJob" };
        var path = ConfigService.ResolveStatePath(config);
        Assert.Contains("S3ConsoleSync", path);
        Assert.EndsWith(".state.json", path);
    }
}
