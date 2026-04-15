using S3ConsoleSync.Services;

namespace S3ConsoleSync.Tests;

public class SchedulerServiceTests
{
    private readonly SchedulerService _sut = new();

    [Fact]
    public void BuildSchtasksArguments_KeepsTaskRunnerCommandIntact_WhenPathsContainSpaces()
    {
        var arguments = SchedulerService.BuildSchtasksArguments(
            "S3ConsoleSync_media-backup",
            @"C:\Program Files\CloudSync\s3consolesync.exe",
            @"C:\Program Files\CloudSync\media-backup.json",
            "DAILY",
            null,
            "02:00",
            null,
            null);

        Assert.Equal("/tr", arguments[3]);
        Assert.Equal(
            @"""C:\Program Files\CloudSync\s3consolesync.exe"" run --config ""C:\Program Files\CloudSync\media-backup.json""",
            arguments[4]);
        Assert.DoesNotContain(arguments, argument => argument == "Files\\CloudSync\\media-backup.json");
    }

    [Fact]
    public void RegisterTask_OnNonWindows_ThrowsPlatformNotSupportedException()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
        {
            // Skip on Windows — actual registration would require elevation.
            return;
        }

        Assert.Throws<PlatformNotSupportedException>(
            () => _sut.RegisterTask("/tmp/test.json", "DAILY 02:00"));
    }

    [Fact]
    public void UnregisterTask_OnNonWindows_ThrowsPlatformNotSupportedException()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
        {
            return;
        }

        Assert.Throws<PlatformNotSupportedException>(
            () => _sut.UnregisterTask("/tmp/test.json"));
    }
}
