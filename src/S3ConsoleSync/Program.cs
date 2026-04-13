using System.CommandLine;
using S3ConsoleSync.Models;
using S3ConsoleSync.Services;
using S3ConsoleSync.Services.Providers;
using Serilog;
using Serilog.Events;

// ─────────────────────────────────────────────────────────────────────────────
// Root command
// ─────────────────────────────────────────────────────────────────────────────
var rootCommand = new RootCommand(
    "S3ConsoleSync — sync local folders to AWS S3, Wasabi or Azure Blob Storage.");

// Shared options
var logDirOption = new Option<string?>(
    "--log-dir",
    "Directory for log files. Defaults to %LOCALAPPDATA%\\S3ConsoleSync\\logs.");

var verboseOption = new Option<bool>(
    "--verbose",
    "Enable verbose (debug-level) logging.");

rootCommand.AddGlobalOption(logDirOption);
rootCommand.AddGlobalOption(verboseOption);

// ─────────────────────────────────────────────────────────────────────────────
// `run` command — execute one or all sync jobs
// ─────────────────────────────────────────────────────────────────────────────
var runCommand = new Command("run", "Execute sync job(s).");

var configOption = new Option<string?>(
    "--config",
    "Path to a single sync config JSON file.");

var configDirOption = new Option<string?>(
    "--config-dir",
    "Directory containing one or more sync config JSON files. All *.json files are executed.");

runCommand.AddOption(configOption);
runCommand.AddOption(configDirOption);

runCommand.SetHandler(async (config, configDir, logDir, verbose) =>
{
    InitialiseLogging(logDir, verbose);

    if (config is null && configDir is null)
    {
        Log.Error("Specify --config <file> or --config-dir <directory>.");
        Environment.Exit(1);
    }

    var configService = new ConfigService();
    var syncService = new SyncService(configService, new FileHashService());
    var configs = new List<SyncConfig>();

    if (config is not null)
    {
        configs.Add(configService.LoadConfig(config));
    }
    else
    {
        configs.AddRange(configService.LoadAllConfigs(configDir!));
        if (configs.Count == 0)
        {
            Log.Warning("No config files found in '{Dir}'.", configDir);
            return;
        }
    }

    foreach (var cfg in configs)
    {
        using var provider = (IStorageProvider)StorageProviderFactory.Create(cfg);
        await syncService.RunAsync(cfg, provider);
    }
},
configOption, configDirOption, logDirOption, verboseOption);

rootCommand.AddCommand(runCommand);

// ─────────────────────────────────────────────────────────────────────────────
// `setup-task` command — register a Windows Scheduled Task
// ─────────────────────────────────────────────────────────────────────────────
var setupTaskCommand = new Command("setup-task",
    "Register a Windows Scheduled Task to run a sync job on a schedule.");

var setupConfigOption = new Option<string>(
    "--config",
    "Path to the sync config JSON file.")
{ IsRequired = true };

var scheduleOption = new Option<string>(
    "--schedule",
    getDefaultValue: () => "DAILY 02:00",
    "Schedule string.  Examples: 'DAILY 02:00' | 'HOURLY' | 'WEEKLY MON 03:00' | 'MONTHLY 1 00:00'");

var taskUserOption = new Option<string?>(
    "--run-as",
    "Windows account to run the task as (default: current user). Use 'SYSTEM' for a service account.");

setupTaskCommand.AddOption(setupConfigOption);
setupTaskCommand.AddOption(scheduleOption);
setupTaskCommand.AddOption(taskUserOption);

setupTaskCommand.SetHandler((setupConfig, schedule, taskUser, logDir, verbose) =>
{
    InitialiseLogging(logDir, verbose);
    var scheduler = new SchedulerService();
    try
    {
        scheduler.RegisterTask(Path.GetFullPath(setupConfig), schedule, taskUser);
    }
    catch (PlatformNotSupportedException ex)
    {
        Log.Warning(ex.Message);
    }
},
setupConfigOption, scheduleOption, taskUserOption, logDirOption, verboseOption);

rootCommand.AddCommand(setupTaskCommand);

// ─────────────────────────────────────────────────────────────────────────────
// `remove-task` command — delete a Windows Scheduled Task
// ─────────────────────────────────────────────────────────────────────────────
var removeTaskCommand = new Command("remove-task",
    "Remove the Windows Scheduled Task for a sync config.");

var removeConfigOption = new Option<string>(
    "--config",
    "Path to the sync config JSON file.")
{ IsRequired = true };

removeTaskCommand.AddOption(removeConfigOption);

removeTaskCommand.SetHandler((removeConfig, logDir, verbose) =>
{
    InitialiseLogging(logDir, verbose);
    var scheduler = new SchedulerService();
    try
    {
        scheduler.UnregisterTask(Path.GetFullPath(removeConfig));
    }
    catch (PlatformNotSupportedException ex)
    {
        Log.Warning(ex.Message);
    }
},
removeConfigOption, logDirOption, verboseOption);

rootCommand.AddCommand(removeTaskCommand);

// ─────────────────────────────────────────────────────────────────────────────
// `list-configs` command — show all config files in a directory
// ─────────────────────────────────────────────────────────────────────────────
var listConfigsCommand = new Command("list-configs",
    "List all sync config files in a directory and display their settings.");

var listDirOption = new Option<string>(
    "--config-dir",
    getDefaultValue: () => "configs",
    "Directory containing sync config JSON files.");

listConfigsCommand.AddOption(listDirOption);

listConfigsCommand.SetHandler((listDir, logDir, verbose) =>
{
    InitialiseLogging(logDir, verbose);
    var configService = new ConfigService();
    var configs = configService.LoadAllConfigs(listDir);

    if (configs.Count == 0)
    {
        Console.WriteLine($"No config files found in '{listDir}'.");
        return;
    }

    Console.WriteLine($"Found {configs.Count} config(s) in '{listDir}':");
    Console.WriteLine();

    foreach (var cfg in configs)
    {
        Console.WriteLine($"  Name            : {cfg.Name}");
        Console.WriteLine($"  Source          : {cfg.SourceFolder}");
        Console.WriteLine($"  Provider        : {cfg.Provider}");
        Console.WriteLine($"  Bucket/Container: {cfg.BucketOrContainer}");
        Console.WriteLine($"  Key Prefix      : {(string.IsNullOrEmpty(cfg.KeyPrefix) ? "(none)" : cfg.KeyPrefix)}");
        Console.WriteLine($"  Storage Tier    : {cfg.StorageTier}");
        Console.WriteLine($"  Delete Removed  : {cfg.DeleteRemovedFiles}");
        Console.WriteLine($"  Exclude Patterns: {(cfg.ExcludePatterns.Count == 0 ? "(none)" : string.Join(", ", cfg.ExcludePatterns))}");
        Console.WriteLine();
    }
},
listDirOption, logDirOption, verboseOption);

rootCommand.AddCommand(listConfigsCommand);

// ─────────────────────────────────────────────────────────────────────────────
// `init-config` command — scaffold a new config file
// ─────────────────────────────────────────────────────────────────────────────
var initConfigCommand = new Command("init-config",
    "Create a new example sync config file.");

var initOutputOption = new Option<string>(
    "--output",
    getDefaultValue: () => "sync-job.json",
    "Output path for the new config file.");

var initProviderOption = new Option<StorageProviderType>(
    "--provider",
    getDefaultValue: () => StorageProviderType.S3,
    "Storage provider (S3, Wasabi, AzureBlob).");

initConfigCommand.AddOption(initOutputOption);
initConfigCommand.AddOption(initProviderOption);

initConfigCommand.SetHandler((output, provider, logDir, verbose) =>
{
    InitialiseLogging(logDir, verbose);

    var config = new SyncConfig
    {
        Name = Path.GetFileNameWithoutExtension(output),
        SourceFolder = @"C:\Backups",
        Provider = provider,
        BucketOrContainer = provider == StorageProviderType.AzureBlob ? "my-container" : "my-bucket",
        Region = "us-east-1",
        StorageTier = provider == StorageProviderType.AzureBlob ? "Hot" : "STANDARD",
        DeleteRemovedFiles = false,
        Credentials = provider == StorageProviderType.AzureBlob
            ? new() { ConnectionString = "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net" }
            : new() { AccessKey = "YOUR_ACCESS_KEY", SecretKey = "YOUR_SECRET_KEY" },
        ExcludePatterns = new List<string> { "*.tmp", "Thumbs.db", ".DS_Store" }
    };

    var configService = new ConfigService();
    configService.SaveConfig(config, output);
    Console.WriteLine($"Config file created: {Path.GetFullPath(output)}");
    Console.WriteLine("Edit the file to set your credentials and source folder, then run:");
    Console.WriteLine($"  s3consolesync run --config \"{output}\"");
},
initOutputOption, initProviderOption, logDirOption, verboseOption);

rootCommand.AddCommand(initConfigCommand);

// ─────────────────────────────────────────────────────────────────────────────
// Execute
// ─────────────────────────────────────────────────────────────────────────────
return await rootCommand.InvokeAsync(args);

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────
static void InitialiseLogging(string? logDir, bool verbose)
{
    var level = verbose ? LogEventLevel.Debug : LogEventLevel.Information;
    LoggingService.Initialise(logDir, level);
}
