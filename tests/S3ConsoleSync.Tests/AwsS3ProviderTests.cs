using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using S3ConsoleSync.Services.Providers;

namespace S3ConsoleSync.Tests;

public class AwsS3ProviderTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public async Task UploadFileAsync_UsesSinglePartUpload_ForSmallFiles()
    {
        var client = new Mock<IAmazonS3>(MockBehavior.Strict);
        var filePath = CreateTempFile(AwsS3Provider.MultipartUploadThresholdBytes - 1);

        client.Setup(c => c.PutObjectAsync(
                It.Is<PutObjectRequest>(r =>
                    r.BucketName == "bucket" &&
                    r.Key == "object-key" &&
                    r.FilePath == filePath),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse { ETag = "single-etag" });

        var provider = new AwsS3Provider(client.Object);

        var etag = await provider.UploadFileAsync("bucket", "object-key", filePath, "STANDARD");

        Assert.Equal("single-etag", etag);
        client.Verify(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadFileAsync_UsesMultipartUpload_ForLargeFiles()
    {
        var client = new Mock<IAmazonS3>(MockBehavior.Strict);
        var filePath = CreateTempFile(AwsS3Provider.MultipartUploadThresholdBytes + 1024);

        client.Setup(c => c.InitiateMultipartUploadAsync(
                It.Is<InitiateMultipartUploadRequest>(r =>
                    r.BucketName == "bucket" &&
                    r.Key == "object-key"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-123" });

        client.Setup(c => c.UploadPartAsync(
                It.Is<UploadPartRequest>(r =>
                    r.UploadId == "upload-123" &&
                    r.FilePath == filePath &&
                    r.PartNumber == 1 &&
                    r.PartSize == AwsS3Provider.MultipartUploadPartSizeBytes),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadPartResponse { ETag = "part-1" });

        client.Setup(c => c.UploadPartAsync(
                It.Is<UploadPartRequest>(r =>
                    r.UploadId == "upload-123" &&
                    r.FilePath == filePath &&
                    r.PartNumber == 2 &&
                    r.PartSize == 1024),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadPartResponse { ETag = "part-2" });

        client.Setup(c => c.CompleteMultipartUploadAsync(
                It.Is<CompleteMultipartUploadRequest>(r =>
                    r.UploadId == "upload-123" &&
                    r.PartETags.Count == 2 &&
                    r.PartETags[0].PartNumber == 1 &&
                    r.PartETags[1].PartNumber == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompleteMultipartUploadResponse { ETag = "multi-etag" });

        var provider = new AwsS3Provider(client.Object);

        var etag = await provider.UploadFileAsync("bucket", "object-key", filePath, "STANDARD");

        Assert.Equal("multi-etag", etag);
        client.Verify(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(c => c.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        client.Verify(c => c.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        foreach (var tempFile in _tempFiles)
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private string CreateTempFile(long length)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"s3consolesync-{Guid.NewGuid():N}.tmp");
        using var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
        _tempFiles.Add(filePath);
        return filePath;
    }
}