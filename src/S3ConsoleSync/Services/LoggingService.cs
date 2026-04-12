using Serilog;
using Serilog.Events;

namespace S3ConsoleSync.Services;

/// <summary>
/// Configures a combined Serilog logger that writes to both the console and a
/// shared rotating log file.  All sync jobs share the same log file so that a
/// single location can be tailed for monitoring.
/// </summary>
public static class LoggingService
{
    private static bool _initialised;

    /// <summary>
    /// Initialise the global Serilog logger.  Safe to call multiple times;
    /// subsequent calls are no-ops.
    /// </summary>
    /// <param name="logDirectory">
    /// Directory where <c>sync.log</c> will be written.
    /// Defaults to <c>%LOCALAPPDATA%\S3ConsoleSync\logs</c> when null or empty.
    /// </param>
    /// <param name="minimumLevel">Minimum log level (default: Information).</param>
    public static void Initialise(
        string? logDirectory = null,
        LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        if (_initialised)
            return;

        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "S3ConsoleSync",
                "logs");
        }

        Directory.CreateDirectory(logDirectory);
        var logFile = Path.Combine(logDirectory, "sync.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.WithProperty("Application", "S3ConsoleSync")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logFile,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        _initialised = true;
    }

    /// <summary>Reset initialisation state (for testing).</summary>
    internal static void Reset()
    {
        Log.CloseAndFlush();
        _initialised = false;
    }
}
