# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors
param([Parameter(Mandatory)][string]$App,[Parameter(Mandatory)][string]$Out,[switch]$AppUpdates)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$App=(Resolve-Path -LiteralPath $App).Path
if (!(Test-Path -LiteralPath (Join-Path $App 'Chippi.exe')) -or !(Test-Path -LiteralPath (Join-Path $App 'Chippi.Elevated.exe'))) { throw 'Use the isolated AdminProbe publish folder, not the real app.' }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'build/fullscreen-check.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Fullscreen app or Zoom in front; run later.' }
$running=wsb list --raw | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or @($running.WindowsSandboxEnvironments).Count -gt 0) { throw 'Another Sandbox is running, or its state could not be read.' }
if ((Test-Path -LiteralPath $Out) -and @(Get-ChildItem -LiteralPath $Out -Force).Count -gt 0) { throw 'Use a new empty output folder.' }
New-Item -ItemType Directory -Path $Out -Force | Out-Null
$Out=(Resolve-Path -LiteralPath $Out).Path
$script=(Resolve-Path -LiteralPath (Join-Path $repo 'tests/Chippi.Elevated.Tests/Sandbox')).Path
$escape={ param($value) [Security.SecurityElement]::Escape($value) }
$wsb=Join-Path $Out 'admin-helper.wsb'
$guestArguments=if ($AppUpdates) { ' -AppUpdates' } else { '' }
@"
<Configuration>
  <Networking>Enable</Networking><ClipboardRedirection>Disable</ClipboardRedirection>
  <AudioInput>Disable</AudioInput><VideoInput>Disable</VideoInput><PrinterRedirection>Disable</PrinterRedirection>
  <MappedFolders>
    <MappedFolder><HostFolder>$(& $escape $App)</HostFolder><SandboxFolder>C:\source-app</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$(& $escape $script)</HostFolder><SandboxFolder>C:\source-probe</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$(& $escape $Out)</HostFolder><SandboxFolder>C:\results-out</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\source-probe\probe.ps1$guestArguments</Command></LogonCommand>
</Configuration>
"@ | Set-Content -LiteralPath $wsb -Encoding UTF8
$sandboxId=[Guid]::NewGuid().ToString()
Set-Content -LiteralPath (Join-Path $Out 'sandbox-id.txt') $sandboxId
try {
    wsb start --id $sandboxId --config (Get-Content -LiteralPath $wsb -Raw)
    if ($LASTEXITCODE -ne 0) { throw 'Sandbox start failed.' }
    Start-Process -FilePath wsb.exe -ArgumentList "connect --id $sandboxId" -WindowStyle Hidden
    $deadline=(Get-Date).AddMinutes(20)
    while (!(Test-Path -LiteralPath (Join-Path $Out 'finished.txt'))) {
        if ((Get-Date) -gt $deadline) { throw 'Sandbox timed out. See guest.log.' }
        Start-Sleep -Seconds 3
    }
    $report=Get-Content -LiteralPath (Join-Path $Out 'results.json') -Raw | ConvertFrom-Json
    $report | ConvertTo-Json -Depth 10
    if (!$report.passed) { throw 'Admin helper Sandbox check failed.' }
}
finally {
    # Stop only the UUID created by this run, including after a failed preflight.
    wsb stop --id $sandboxId
    if ($LASTEXITCODE -ne 0) { Write-Warning "Sandbox cleanup failed for $sandboxId" }
}
