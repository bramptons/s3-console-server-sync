using S3ConsoleSync.Services;

namespace S3ConsoleSync.Tests;

public class SchedulerServiceTests
{
    private readonly SchedulerService _sut = new();

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
