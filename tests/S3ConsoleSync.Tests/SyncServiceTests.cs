using Moq;
using S3ConsoleSync.Models;
using S3ConsoleSync.Services;
using S3ConsoleSync.Services.Providers;

namespace S3ConsoleSync.Tests;

public class SyncServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _configService;
    private readonly FileHashService _hashService;
    private readonly SyncService _sut;

    public SyncServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"s3sync_svc_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _configService = new ConfigService();
        _hashService = new FileHashService();
        _sut = new SyncService(_configService, _hashService);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private SyncConfig MakeConfig(bool deleteRemoved = false) => new SyncConfig
    {
        Name = "TestSync",
        SourceFolder = _tempDir,
        BucketOrContainer = "my-bucket",
        StorageTier = "STANDARD",
        DeleteRemovedFiles = deleteRemoved,
        StateFilePath = Path.Combine(_tempDir, ".state.json")
    };

    private string WriteFile(string relativePath, string contents = "test content")
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
        return fullPath;
    }

    [Fact]
    public async Task RunAsync_NewFiles_UploadsAllFiles()
    {
        WriteFile("a.txt");
        WriteFile("sub/b.txt");

        var provider = new Mock<IStorageProvider>();
        provider.Setup(p => p.UploadFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var result = await _sut.RunAsync(MakeConfig(), provider.Object);

        Assert.True(result.Success);
        Assert.Equal(2, result.FilesUploaded);
        Assert.Equal(0, result.FilesSkipped);
        provider.Verify(p => p.UploadFileAsync(
            "my-bucket", It.IsAny<string>(), It.IsAny<string>(), "STANDARD", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_UnchangedFiles_SkipsUpload()
    {
        var filePath = WriteFile("unchanged.txt", "fixed content");

        // Pre-populate the state so the file looks unchanged.
        var config = MakeConfig();
        var info = new FileInfo(filePath);
        var state = new SyncState();
        state.Files["unchanged.txt"] = new FileState
        {
            ContentMd5 = _hashService.ComputeMd5(filePath),
            SizeBytes = info.Length,
            LastModifiedUtc = info.LastWriteTimeUtc,
            ETag = "\"original-etag\""
        };
        _configService.SaveState(config, state);

        var provider = new Mock<IStorageProvider>();

        var result = await _sut.RunAsync(config, provider.Object);

        Assert.True(result.Success);
        Assert.Equal(0, result.FilesUploaded);
        Assert.Equal(1, result.FilesSkipped);
        provider.Verify(p => p.UploadFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_ChangedFile_ReUploadsFile()
    {
        var filePath = WriteFile("changing.txt", "version1");
        var config = MakeConfig();

        // Save stale state (wrong MD5 / size).
        var state = new SyncState();
        state.Files["changing.txt"] = new FileState
        {
            ContentMd5 = "000000000000000000000000000000",
            SizeBytes = 1,
            LastModifiedUtc = DateTime.UtcNow.AddDays(-1)
        };
        _configService.SaveState(config, state);

        var provider = new Mock<IStorageProvider>();
        provider.Setup(p => p.UploadFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-etag");

        var result = await _sut.RunAsync(config, provider.Object);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesUploaded);
        Assert.Equal(0, result.FilesSkipped);
    }

    [Fact]
    public async Task RunAsync_DeleteRemovedFiles_DeletesOrphanedRemoteFiles()
    {
        WriteFile("present.txt");

        var config = MakeConfig(deleteRemoved: true);

        var provider = new Mock<IStorageProvider>();
        provider.Setup(p => p.UploadFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Remote has an extra file that no longer exists locally.
        provider.Setup(p => p.ListObjectKeysAsync("my-bucket", "", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)new[] { "present.txt", "orphan.txt" });

        var result = await _sut.RunAsync(config, provider.Object);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesDeleted);
        provider.Verify(p => p.DeleteObjectAsync("my-bucket", "orphan.txt", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_DeleteRemovedFalse_DoesNotDeleteAnything()
    {
        WriteFile("file.txt");
        var config = MakeConfig(deleteRemoved: false);

        var provider = new Mock<IStorageProvider>();
        provider.Setup(p => p.UploadFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var result = await _sut.RunAsync(config, provider.Object);

        Assert.True(result.Success);
        Assert.Equal(0, result.FilesDeleted);
        provider.Verify(p => p.ListObjectKeysAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        provider.Verify(p => p.DeleteObjectAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_MissingSourceFolder_ReturnsSuccessWithNoFiles()
    {
        var config = MakeConfig();
        config.SourceFolder = Path.Combine(_tempDir, "does-not-exist");

        var provider = new Mock<IStorageProvider>();

        var result = await _sut.RunAsync(config, provider.Object);

        Assert.True(result.Success);
        Assert.Equal(0, result.FilesUploaded);
    }

    [Fact]
    public async Task RunAsync_ExcludePatterns_SkipsMatchingFiles()
    {
        WriteFile("important.txt");
        WriteFile("notes.tmp");

        var config = MakeConfig();
        config.ExcludePatterns = new List<string> { "*.tmp" };

        var provider = new Mock<IStorageProvider>();
        provider.Setup(p => p.UploadFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var result = await _sut.RunAsync(config, provider.Object);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesUploaded);
        provider.Verify(p => p.UploadFileAsync(
            "my-bucket", "important.txt", It.IsAny<string>(), "STANDARD", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithKeyPrefix_PrependsPrefix()
    {
        WriteFile("doc.txt");

        var config = MakeConfig();
        config.KeyPrefix = "server1/backups";

        var provider = new Mock<IStorageProvider>();
        provider.Setup(p => p.UploadFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        await _sut.RunAsync(config, provider.Object);

        provider.Verify(p => p.UploadFileAsync(
            "my-bucket", "server1/backups/doc.txt", It.IsAny<string>(), "STANDARD", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_PersistsStateAfterSuccessfulRun()
    {
        WriteFile("persisted.txt", "hello");
        var config = MakeConfig();

        var provider = new Mock<IStorageProvider>();
        provider.Setup(p => p.UploadFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("saved-etag");

        await _sut.RunAsync(config, provider.Object);

        var state = _configService.LoadState(config);
        Assert.True(state.Files.ContainsKey("persisted.txt"));
        Assert.Equal("saved-etag", state.Files["persisted.txt"].ETag);
        Assert.True(state.LastSyncUtc > DateTime.UtcNow.AddMinutes(-1));
    }
}
