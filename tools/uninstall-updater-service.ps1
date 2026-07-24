#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Stops and removes the SmartCodeGen Wikipedia updater service.

.DESCRIPTION
    Removing the service stops the store advancing — nothing else updates it. The store itself and
    its status file are left alone.
#>
[CmdletBinding()]
param(
    [string] $ServiceName = 'SmartCodeGenWikipediaUpdater',
    [string] $EventSource = 'SmartCodeGen Wikipedia Updater',
    [switch] $KeepEventSource
)

$ErrorActionPreference = 'Stop'

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service '$ServiceName' is not installed. Nothing to do."
}
else {
    if ($existing.Status -ne 'Stopped') {
        Write-Host "Stopping '$ServiceName'"
        Stop-Service -Name $ServiceName -Force
    }

    Write-Host "Deleting '$ServiceName'"
    sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe delete failed with exit code $LASTEXITCODE." }
}

if (-not $KeepEventSource -and [System.Diagnostics.EventLog]::SourceExists($EventSource)) {
    Write-Host "Removing Event Log source '$EventSource'"
    # The .NET API, not Remove-EventLog: see install-updater-service.ps1 for why the cmdlet form
    # risks shadowing Get-Service/Stop-Service/Start-Service used above via the Windows PowerShell
    # compatibility session.
    [System.Diagnostics.EventLog]::DeleteEventSource($EventSource)
}

Write-Host "Done. The store and its update-status.json were left in place."
