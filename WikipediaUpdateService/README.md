# WikipediaUpdateService

A Windows Service that keeps the offline Wikipedia store up to date. It is the **only** process that
applies the daily increments: the `CodeGenerator` CLI does not. Without it the store stops advancing.

This is a .NET worker (`Microsoft.NET.Sdk.Worker`, `net10.0`) that references
[`DomainDiscovery`](../DomainDiscovery/README.md), which holds the real work (`IncrementalUpdater`,
`WikipediaStore`, `StoreUpdateStatusFile`). The service is wiring.

## Main types

| Type | Role |
| --- | --- |
| `Program` | Hosts the worker as the service `SmartCodeGenWikipediaUpdater`. Pins the content root to the executable's folder, so console mode and service mode read the same `appsettings.json`. |
| `UpdateWorker` | The `BackgroundService`: checks for new increments every `IntervalHours` and applies them, writing progress to the status file. |
| `ServiceOptions` | The `Updater` settings and their defaults (`ApplyDefaults()`). |
| `EventLogUpdateLog` | The `IUpdateLog` that writes to the Windows Event Log. |

## Settings (`appsettings.json`, section `Updater`)

| Setting | Default |
| --- | --- |
| `StorePath` | `null`: `WikipediaStoreLocation.Resolve()` (`StorePath` in `discovery.json`, else the shared `%ProgramData%\SmartCodeGenerator\discovery.json`, else `%ProgramData%\SmartCodeGenerator\Wikipedia\enwiki-full.db`). |
| `WorkingDirectory` | `incr` beside the store. |
| `IntervalHours` | `24` (a value of 0 or less becomes 24). |
| `StatusFilePath` | Beside the store (`StoreUpdateStatusFile.BesideStore`). |

## How the CLI uses it

The CLI never writes to the store. At startup it reads the status file and detects the service
(`UpdaterServicePresence`), and prints a store line only when something is wrong; `status` always
shows it (`CodeGenerator/App/StoreStatusReport.cs`).

## Install and remove

From an elevated PowerShell:

```powershell
.\tools\install-updater-service.ps1      # publishes to artifacts\updater-service, registers the Event Log source, installs and starts the service
.\tools\uninstall-updater-service.ps1
```

The service runs as LocalSystem with a delayed-automatic start, so it is right only while the store
is on a local disk. To run it from a console for diagnosis:
`dotnet run --project WikipediaUpdateService`.
