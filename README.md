# s3-console-server-sync

A cross-platform .NET 8 console application that syncs a local drive or folder to **AWS S3**, **Wasabi Hot Storage**, or **Azure Blob Storage**.

## Features

| Feature | Detail |
|---|---|
| **Multi-provider** | AWS S3, Wasabi (S3-compatible), Azure Blob Storage |
| **Diff uploads** | MD5 content hash + file size + last-modified timestamp — only changed files are uploaded |
| **Delete removed files** | Optionally remove cloud objects when the local file no longer exists |
| **Storage tier / class** | Configurable per job (e.g. `STANDARD`, `STANDARD_IA`, `GLACIER` for S3/Wasabi; `Hot`, `Cool`, `Archive` for Azure) |
| **Flat JSON config** | One JSON file per sync job — easy to read, edit and version-control |
| **Combined log file** | All sync jobs write to a single rotating log file (`%LOCALAPPDATA%\S3ConsoleSync\logs\sync.log`) |
| **Scheduling** | Uses **Windows Task Scheduler** (`schtasks.exe`) — the app provides a `setup-task` command to register scheduled runs. On Linux/macOS, use `cron` or a `systemd` timer. |
| **Exclude patterns** | Glob patterns to skip files (e.g. `*.tmp`, `Thumbs.db`) |
| **Key prefix** | Optionally store all objects under a "virtual folder" inside the bucket/container |

## Why Windows Task Scheduler (not an internal scheduler)?

A console application is optimally paired with the OS scheduler because:
- The process does **not** need to stay resident between runs — no wasted memory or risk of handle leaks.
- The OS handles missed runs, retries, and running under a specific service account.
- `schtasks.exe` is available on every Windows edition with no additional dependencies.

On Linux/macOS, use `cron` or a `systemd` timer to call the `run` command.

---

## Quick Start

### 1. Install

```bash
# Build a self-contained executable (Windows x64)
dotnet publish src/S3ConsoleSync -c Release -r win-x64 --self-contained -o publish/

# Or just run via dotnet
dotnet run --project src/S3ConsoleSync -- --help
```

### 2. Create a config file

```bash
# Scaffold an S3 config
dotnet run --project src/S3ConsoleSync -- init-config --output my-backup.json --provider S3

# Scaffold a Wasabi config
dotnet run --project src/S3ConsoleSync -- init-config --output wasabi-backup.json --provider Wasabi

# Scaffold an Azure Blob config
dotnet run --project src/S3ConsoleSync -- init-config --output azure-backup.json --provider AzureBlob
```

Edit the generated file to fill in your credentials and source folder.

### 3. Run a sync

```bash
# Run a single config
dotnet run --project src/S3ConsoleSync -- run --config my-backup.json

# Run all configs in a directory
dotnet run --project src/S3ConsoleSync -- run --config-dir configs/

# Verbose output
dotnet run --project src/S3ConsoleSync -- run --config my-backup.json --verbose
```

### 4. Schedule with Windows Task Scheduler

```bash
# Run daily at 02:00
dotnet run --project src/S3ConsoleSync -- setup-task --config my-backup.json --schedule "DAILY 02:00"

# Run every Monday at 03:30
dotnet run --project src/S3ConsoleSync -- setup-task --config my-backup.json --schedule "WEEKLY MON 03:30"

# Run hourly as SYSTEM
dotnet run --project src/S3ConsoleSync -- setup-task --config my-backup.json --schedule HOURLY --run-as SYSTEM

# Remove a scheduled task
dotnet run --project src/S3ConsoleSync -- remove-task --config my-backup.json
```

### 5. List configs

```bash
dotnet run --project src/S3ConsoleSync -- list-configs --config-dir configs/
```

---

## Config File Reference

```jsonc
{
  // Friendly name for this sync job (used in logs and task name)
  "Name": "DocumentsBackup",

  // Local folder or drive root to sync
  "SourceFolder": "C:\\Users\\YourName\\Documents",

  // Provider: "S3" | "Wasabi" | "AzureBlob"
  "Provider": "S3",

  // S3 bucket name or Azure container name
  "BucketOrContainer": "my-backup-bucket",

  // Optional key prefix ("virtual folder") inside the bucket/container
  "KeyPrefix": "documents",

  // AWS region (e.g. "us-east-1"); also used for Wasabi endpoint selection
  "Region": "us-east-1",

  "Credentials": {
    // AWS S3 / Wasabi
    "AccessKey": "YOUR_ACCESS_KEY",
    "SecretKey": "YOUR_SECRET_KEY",

    // Azure Blob Storage
    "ConnectionString": "",

    // Wasabi: override the default regional endpoint (optional)
    "CustomEndpoint": ""
  },

  // When true, objects deleted locally are removed from the cloud
  "DeleteRemovedFiles": false,

  // Storage class / tier
  // S3/Wasabi: STANDARD | STANDARD_IA | ONEZONE_IA | INTELLIGENT_TIERING | GLACIER | GLACIER_IR | DEEP_ARCHIVE
  // Azure:     Hot | Cool | Archive | Cold
  "StorageTier": "STANDARD",

  // Glob patterns for files to exclude
  "ExcludePatterns": ["*.tmp", "*.bak", "Thumbs.db", ".DS_Store"],

  // Path to the state file used for change detection.
  // Leave empty to use %LOCALAPPDATA%\S3ConsoleSync\<Name>.state.json
  "StateFilePath": ""
}
```

See the `configs/` directory for ready-to-edit example files.

---

## Logging

All sync jobs share a single rotating log file:

```
%LOCALAPPDATA%\S3ConsoleSync\logs\sync-YYYYMMDD.log
```

Up to 30 days of log files are retained. Override the log directory with `--log-dir`:

```bash
s3consolesync run --config my-backup.json --log-dir D:\Logs\S3Sync
```

---

## Building & Testing

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Publish self-contained Windows binary
dotnet publish src/S3ConsoleSync -c Release -r win-x64 --self-contained -o publish/
```

