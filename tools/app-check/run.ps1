# Builds what app-check drives, then runs it. See README.md.
#
#   powershell -ExecutionPolicy Bypass -File tools\app-check\run.ps1            # every case
#   powershell -ExecutionPolicy Bypass -File tools\app-check\run.ps1 -Only sql  # cases whose id contains "sql"
#
# The WPF and WinForms checks drive real windows, so the desktop must be unlocked while it runs.

param(
    [string] $Only,
    [switch] $NoLaunch,
    [string] $Runs = 'C:\ac\runs'
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here '..\..')

Push-Location $here
try {
    if (-not (Test-Path node_modules)) { npm install --no-audit --no-fund | Out-Null }

    # Release, and rebuilt every time: `dotnet test` elsewhere leaves this bin stale, and a stale
    # CLI makes a fixed generator look unfixed.
    Write-Host '== CLI'
    dotnet build (Join-Path $repo 'CodeGenerator\CodeGenerator.csproj') -c Release --nologo -v quiet
    if ($LASTEXITCODE) { throw 'CodeGenerator failed to build' }

    Write-Host '== window driver'
    dotnet build (Join-Path $repo 'tools\demo-video\WpfDriver\WpfDriver.csproj') -c Release --nologo -v quiet
    if ($LASTEXITCODE) { throw 'WpfDriver failed to build' }

    $arguments = @('check.mjs', '--runs', $Runs)
    if ($Only) { $arguments += @('--only', $Only) }
    if ($NoLaunch) { $arguments += '--no-launch' }

    node @arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
