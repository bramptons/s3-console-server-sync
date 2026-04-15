using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace S3ConsoleSync.Services;

/// <summary>
/// Manages Windows Task Scheduler entries for S3ConsoleSync.
///
/// Scheduling rationale
/// ─────────────────────
/// A console application is best scheduled via Windows Task Scheduler rather
/// than an internal timer because:
///   • The process does not need to remain resident between runs.
///   • The OS handles missed runs, retries, and run-as-service-account.
///   • No risk of memory leaks or handle exhaustion over long uptime.
///
/// This service uses <c>schtasks.exe</c> (available on all Windows editions)
/// so that the application has no additional dependencies.
/// </summary>
public class SchedulerService
{
    private const string TaskNamePrefix = "S3ConsoleSync_";

    /// <summary>
    /// Register (or update) a Windows scheduled task that will run the
    /// currently executing executable with the <c>run --config &lt;configPath&gt;</c>
    /// arguments on the given schedule.
    /// </summary>
    /// <param name="configPath">Absolute path to the sync config JSON file.</param>
    /// <param name="schedule">
    /// A schedule expressed as one of:
    ///   • <c>DAILY HH:mm</c>   — e.g. "DAILY 02:00"
    ///   • <c>HOURLY</c>
    ///   • <c>WEEKLY DayOfWeek HH:mm</c> — e.g. "WEEKLY MON 03:30"
    ///   • <c>MONTHLY day HH:mm</c> — e.g. "MONTHLY 1 00:00"
    /// </param>
    /// <param name="taskUser">
    /// Windows account to run the task as.  Defaults to the current user.
    /// Use "SYSTEM" for a service account (no password required).
    /// </param>
    public void RegisterTask(string configPath, string schedule, string? taskUser = null)
    {
        EnsureWindows();

        var exePath = Process.GetCurrentProcess().MainModule?.FileName
                      ?? throw new InvalidOperationException("Cannot determine executable path.");

        var taskName = BuildTaskName(configPath);
        var (scheduleType, schedMod, startTime, day) = ParseSchedule(schedule);

        var arguments = BuildSchtasksArguments(
            taskName, exePath, configPath, scheduleType, schedMod, startTime, day, taskUser);

        Log.Information("Registering scheduled task '{Task}'  schedule='{Schedule}'", taskName, schedule);
        RunSchtasks(arguments);
        Log.Information("Scheduled task '{Task}' registered successfully.", taskName);
    }

    /// <summary>Remove the scheduled task for the given config, if it exists.</summary>
    public void UnregisterTask(string configPath)
    {
        EnsureWindows();
        var taskName = BuildTaskName(configPath);
        Log.Information("Removing scheduled task '{Task}'", taskName);
        RunSchtasks(["/delete", "/tn", taskName, "/f"]);
        Log.Information("Scheduled task '{Task}' removed.", taskName);
    }

    /// <summary>List all S3ConsoleSync scheduled tasks.</summary>
    public void ListTasks()
    {
        EnsureWindows();
        RunSchtasks(["/query", "/fo", "LIST", "/v", "/tn", TaskNamePrefix]);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string BuildTaskName(string configPath)
    {
        var name = Path.GetFileNameWithoutExtension(configPath);
        var safe = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
        return $"{TaskNamePrefix}{safe}";
    }

    internal static string BuildTaskRunCommand(string exePath, string configPath)
    {
        return $"\"{exePath}\" run --config \"{configPath}\"";
    }

    internal static IReadOnlyList<string> BuildSchtasksArguments(
        string taskName,
        string exePath,
        string configPath,
        string scheduleType,
        string? schedMod,
        string? startTime,
        string? day,
        string? taskUser)
    {
        var arguments = new List<string>
        {
            "/create",
            "/tn",
            taskName,
            "/tr",
            BuildTaskRunCommand(exePath, configPath),
            "/sc",
            scheduleType,
            "/f"
        };

        if (!string.IsNullOrEmpty(schedMod))
        {
            arguments.Add("/mo");
            arguments.Add(schedMod);
        }

        if (!string.IsNullOrEmpty(startTime))
        {
            arguments.Add("/st");
            arguments.Add(startTime);
        }

        if (!string.IsNullOrEmpty(day))
        {
            arguments.Add("/d");
            arguments.Add(day);
        }

        if (!string.IsNullOrEmpty(taskUser))
        {
            arguments.Add("/ru");
            arguments.Add(taskUser.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase)
                ? "SYSTEM"
                : taskUser);
        }

        return arguments;
    }

    private static (string type, string? mod, string? startTime, string? day) ParseSchedule(string schedule)
    {
        var parts = schedule.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts[0].ToUpperInvariant() switch
        {
            "DAILY"   => ("DAILY", null, parts.Length > 1 ? parts[1] : "00:00", null),
            "HOURLY"  => ("HOURLY", null, null, null),
            "WEEKLY"  => ("WEEKLY", null,
                          parts.Length > 2 ? parts[2] : "00:00",
                          parts.Length > 1 ? parts[1].ToUpperInvariant() : "MON"),
            "MONTHLY" => ("MONTHLY", null,
                          parts.Length > 2 ? parts[2] : "00:00",
                          parts.Length > 1 ? parts[1] : "1"),
            _         => throw new ArgumentException(
                             $"Unknown schedule format: '{schedule}'. " +
                             "Use DAILY HH:mm | HOURLY | WEEKLY DayOfWeek HH:mm | MONTHLY day HH:mm")
        };
    }

    private static void RunSchtasks(IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException("Failed to start schtasks.exe");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(stdout))
            Log.Debug("schtasks: {Output}", stdout.Trim());

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"schtasks.exe exited with code {process.ExitCode}: {stderr.Trim()}");
    }

    private static void EnsureWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException(
                "Task Scheduler integration is only available on Windows. " +
                "On Linux/macOS, use cron or a systemd timer to schedule: " +
                "  s3consolesync run --config <path>");
    }
}
