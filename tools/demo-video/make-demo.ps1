# Makes tools/demo-video/build-app-demo.mp4 from scratch. See README.md.
#
#   powershell -ExecutionPolicy Bypass -File tools\demo-video\make-demo.ps1
#
# 1. Narration: one audio file per scene, and caption timings (make_voice.py).
# 2. A real `build app` workflow, served by the DemoVideoHost test, in a fresh workspace.
# 3. The recording: record.mjs drives that page from the CSV to the running Blazor app.
# 4. Tidying up: the generated app is stopped and the host told to finish.
# 5. Assembly: picture and voice into an MP4, captions into a VTT (assemble.py).

param(
    # Short on purpose: a generated project under a long path hits MAX_PATH during its build.
    [string] $Workspace = 'C:\dv',
    [switch] $SkipVoice
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here '..\..')
$csv = Join-Path $here 'data\orders.csv'

Push-Location $here
try {
    if (-not (Test-Path node_modules)) { npm install --no-audit --no-fund | Out-Null }

    if (-not $SkipVoice) {
        Write-Host '== narration'
        python make_voice.py
        if ($LASTEXITCODE) { throw 'make_voice.py failed' }
    }

    Write-Host "== fresh workspace $Workspace"
    Get-Process | Where-Object { $_.Path -like "$Workspace\*" } | Stop-Process -Force
    if (Test-Path $Workspace) { Remove-Item -Recurse -Force $Workspace }

    Write-Host '== host'
    $env:DEMO_VIDEO_ROOT = $Workspace
    $env:DEMO_VIDEO_CSV = $csv
    $host_ = Start-Process dotnet -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $here 'out\host.log') -ArgumentList @(
        'test', (Join-Path $repo 'CodeGenerator.Tests\CodeGenerator.Tests.csproj'), '-c', 'Release',
        '--filter', 'FullyQualifiedName~DemoVideoHost')

    $url = Join-Path $Workspace 'url.txt'
    $deadline = (Get-Date).AddMinutes(8)
    while (-not (Test-Path $url)) {
        if ((Get-Date) -gt $deadline -or $host_.HasExited) { throw 'The host did not start: see out\host.log.' }
        Start-Sleep -Seconds 2
    }

    Write-Host '== recording'
    node record.mjs $Workspace 2>&1 | Tee-Object -FilePath (Join-Path $here 'out/record.log')
    $recorded = $LASTEXITCODE

    Write-Host '== tidying up'
    Get-Process | Where-Object { $_.Path -like "$Workspace\*" } | Stop-Process -Force
    New-Item -ItemType File -Force (Join-Path $Workspace 'stop.txt') | Out-Null

    # The whole tree if it has not gone by itself: a host left behind locks the test binary, and
    # the next run's build fails with MSB3027 before it can start a host at all.
    if (-not $host_.WaitForExit(60000)) { taskkill /PID $host_.Id /T /F | Out-Null }

    if ($recorded) { throw 'record.mjs failed: see out\failed.png for the frame it failed on.' }

    Write-Host '== assembly'
    python assemble.py
    if ($LASTEXITCODE) { throw 'assemble.py failed' }
}
finally {
    Remove-Item Env:DEMO_VIDEO_ROOT, Env:DEMO_VIDEO_CSV -ErrorAction SilentlyContinue
    Pop-Location
}
