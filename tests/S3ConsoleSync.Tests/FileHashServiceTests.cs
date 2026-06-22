using S3ConsoleSync.Models;
using S3ConsoleSync.Services;

namespace S3ConsoleSync.Tests;

public class FileHashServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileHashService _sut;

    public FileHashServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"s3hash_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _sut = new FileHashService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private string WriteFile(string name, string contents)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void ComputeMd5_SameContents_SameHash()
    {
        var path1 = WriteFile("a.txt", "hello world");
        var path2 = WriteFile("b.txt", "hello world");

        Assert.Equal(_sut.ComputeMd5(path1), _sut.ComputeMd5(path2));
    }

    [Fact]
    public void ComputeMd5_DifferentContents_DifferentHash()
    {
        var path1 = WriteFile("c.txt", "hello");
        var path2 = WriteFile("d.txt", "world");

        Assert.NotEqual(_sut.ComputeMd5(path1), _sut.ComputeMd5(path2));
    }

    [Fact]
    public void ComputeMd5_ReturnsLowercaseHex()
    {
        var path = WriteFile("e.txt", "test");
        var hash = _sut.ComputeMd5(path);
        Assert.Matches("^[0-9a-f]{32}$", hash);
    }

    [Fact]
    public void HasChanged_NoPreviousState_ReturnsTrue()
    {
        var path = WriteFile("new.txt", "data");
        Assert.True(_sut.HasChanged(path, null));
    }

    [Fact]
    public void HasChanged_SameFile_ReturnsFalse()
    {
        var path = WriteFile("same.txt", "data");
        var info = new FileInfo(path);
        var state = new FileState
        {
            ContentMd5 = _sut.ComputeMd5(path),
            SizeBytes = info.Length,
            LastModifiedUtc = info.LastWriteTimeUtc
        };

        Assert.False(_sut.HasChanged(path, state));
    }

    [Fact]
    public void HasChanged_SizeDiffers_ReturnsTrue()
    {
        var path = WriteFile("changed_size.txt", "original");
        var state = new FileState
        {
            ContentMd5 = _sut.ComputeMd5(path),
            SizeBytes = 9999,                          // wrong size
            LastModifiedUtc = new FileInfo(path).LastWriteTimeUtc
        };

        Assert.True(_sut.HasChanged(path, state));
    }

    [Fact]
    public void HasChanged_ModifiedTimeDiffersSameContent_ReturnsFalse()
    {
        var path = WriteFile("changed_mtime.txt", "same content");
        var info = new FileInfo(path);
        var state = new FileState
        {
            ContentMd5 = _sut.ComputeMd5(path),
            SizeBytes = info.Length,
            LastModifiedUtc = info.LastWriteTimeUtc.AddHours(-1) // stale timestamp
        };

        Assert.False(_sut.HasChanged(path, state));
    }

    [Fact]
    public void HasChanged_SameSizeDifferentContent_ReturnsTrue()
    {
        var path = WriteFile("same_size_changed_content.txt", "video-episode-01");
        var info = new FileInfo(path);
        var state = new FileState
        {
            ContentMd5 = _sut.ComputeMd5(path),
            SizeBytes = info.Length,
            LastModifiedUtc = info.LastWriteTimeUtc
        };

        File.WriteAllText(path, "video-episode-02");
        File.SetLastWriteTimeUtc(path, state.LastModifiedUtc);

        Assert.True(_sut.HasChanged(path, state));
    }
}
