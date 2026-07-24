#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Publishes and installs the SmartCodeGen Wikipedia updater as a Windows Service.

.DESCRIPTION
    The service is the SOLE owner of the daily Wikipedia increment update — the CodeGenerator CLI
    no longer does it. Without this service installed, the store stops advancing entirely.

    Runs as LocalSystem. That is correct only while the store is on LOCAL disk: LocalSystem
    authenticates off-box as the machine account, so a store moved to a network share would need a
    domain account with rights to that share instead.

    Start type is delayed-auto rather than auto: this still runs "when the computer starts", but it
    keeps an ~800 MB download from competing with the boot storm.
#>
[CmdletBinding()]
param(
    [string] $ServiceName = 'SmartCodeGenWikipediaUpdater',
    [string] $EventSource = 'SmartCodeGen Wikipedia Updater',
    [string] $PublishDirectory = (Join-Path $PSScriptRoot '..\artifacts\updater-service')
)

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot '..\WikipediaUpdateService\WikipediaUpdateService.csproj'
$publishPath = [System.IO.Path]::GetFullPath($PublishDirectory)

Write-Host "Publishing $projectPath -> $publishPath"
dotnet publish $projectPath -c Release -o $publishPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$exePath = Join-Path $publishPath 'WikipediaUpdateService.exe'
if (-not (Test-Path $exePath)) { throw "Published executable not found at $exePath." }

if (-not [System.Diagnostics.EventLog]::SourceExists($EventSource)) {
    Write-Host "Registering Event Log source '$EventSource'"
    # The .NET API, not New-EventLog: that cmdlet does not exist natively in PowerShell 7 and pwsh
    # proxies it through a Windows PowerShell compatibility session, which also imports proxy
    # functions that SHADOW Get-Service/Stop-Service/Start-Service for the rest of this script (all
    # used below). Where Windows PowerShell is absent or blocked by policy, that proxy throws, and
    # $ErrorActionPreference = 'Stop' aborts after dotnet publish already ran and before sc.exe
    # create — several hundred MB published, nothing installed.
    $sourceData = New-Object System.Diagnostics.EventSourceCreationData $EventSource, 'Application'
    [System.Diagnostics.EventLog]::CreateEventSource($sourceData)
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$ServiceName' already exists; stopping and removing it first."
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Null
    # Heuristic: allow the Service Control Manager time to finish removing the service. If a handle
    # to the Services database is still open (e.g., services.msc or Process Explorer on the target),
    # the service lingers "marked for deletion" and the subsequent create fails with that message;
    # remedy is to close that window and re-run.
    Start-Sleep -Seconds 2
}

Write-Host "Creating service '$ServiceName'"
sc.exe create $ServiceName binPath= "`"$exePath`"" start= delayed-auto obj= LocalSystem | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed with exit code $LASTEXITCODE." }

sc.exe description $ServiceName "Keeps the SmartCodeGen Wikipedia store up to date with daily increments." | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe description failed with exit code $LASTEXITCODE." }

# Restart on failure so a crash does not silently end updating: 1 min, 5 min, then 15 min,
# with the failure count resetting after a day.
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/300000/restart/900000 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe failure failed with exit code $LASTEXITCODE." }

Write-Host "Starting service"
Start-Service -Name $ServiceName

Get-Service -Name $ServiceName | Format-List Name, Status, StartType
Write-Host "Done. Progress is published to the store's update-status.json; milestones go to the Application event log."
