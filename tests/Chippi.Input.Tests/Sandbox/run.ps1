# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

<#
.SYNOPSIS
Runs the input hub's real-hook checks in Windows Sandbox (docs/phase-6/architecture.md section 8). See README.md here.

.PARAMETER App
A folder holding a published, self-contained Chippi.exe (the Sandbox has no .NET).

.PARAMETER Out
A folder for the results; emptied first. The Sandbox writes probe.log, results.json, the target's log, Chippi's logs and,
when the build has the hub, hook-timings.csv there.

.PARAMETER Checks
Any of: radial (the Radial menu's hold, flick, click, drag and Esc), cost (Chippi's CPU over 60 s of the mouse moving, three
times), watchdog (Debug builds of the hub only: both hooks are dropped behind the hub's back 45 s after start, and the
Radial menu must work again after the mouse and keys have moved), pause (Debug builds only: a borderless window over
the screen stands in for a game; the pause, Always on, Pause in these apps and the Radial menu's Open over fullscreen apps), debounce (Debounce on SendInput: pairs 2 ms and 60 ms apart), timestamps (the hook's own timestamp against Stopwatch), shortcuts (Shortcuts: Back, Forward and Middle, the side wheel's tilts, a key, Win combinations, an app left alone), scroll (Smooth scrolling: notch totals, glide, Ctrl+wheel, leaving the window, a game in front, Linear, per-app speeds), switcher (the App switcher by shortcut and Radial slice: Enter, Esc, search, arrows, where focus lands).
#>
param(
    [Parameter(Mandatory)][string]$App,
    [Parameter(Mandatory)][string]$Out,
    [string[]]$Checks = @('radial', 'cost'),
    [int]$TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'
$App = (Resolve-Path -LiteralPath $App).Path
if (-not (Test-Path -LiteralPath (Join-Path $App 'Chippi.exe'))) { throw "No Chippi.exe in $App" }
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path

# The Sandbox's window comes to the front: never over a game or a call.
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'build\fullscreen-check.ps1')
if ($LASTEXITCODE -ne 0) { throw 'A fullscreen app or Zoom is in front. Run this later.' }
if (Get-Process -Name WindowsSandbox, WindowsSandboxClient, WindowsSandboxRemoteSession -ErrorAction SilentlyContinue) {
    throw 'Windows Sandbox is already running, and only one can. Wait for it to close.'
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path -LiteralPath $Out).Path
Get-ChildItem -LiteralPath $Out -Force | Remove-Item -Recurse -Force
Set-Content -LiteralPath (Join-Path $Out 'checks.txt') -Value (($Checks | ForEach-Object { $_ -split ',' }) -join ',') -Encoding ASCII

$wsb = Join-Path ([IO.Path]::GetTempPath()) ("chippi-input-" + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.wsb')
@"
<Configuration>
  <Networking>Disable</Networking>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <AudioInput>Disable</AudioInput>
  <VideoInput>Disable</VideoInput>
  <PrinterRedirection>Disable</PrinterRedirection>
  <MappedFolders>
    <MappedFolder><HostFolder>$App</HostFolder><SandboxFolder>C:\chippi</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$PSScriptRoot</HostFolder><SandboxFolder>C:\probe</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$Out</HostFolder><SandboxFolder>C:\results</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\probe\probe.ps1</Command></LogonCommand>
</Configuration>
"@ | Set-Content -LiteralPath $wsb -Encoding UTF8

$started = Get-Date
Start-Process -FilePath 'WindowsSandbox.exe' -ArgumentList "`"$wsb`""
$done = Join-Path $Out 'done.txt'
while (-not (Test-Path -LiteralPath $done)) {
    if ((Get-Date) - $started -gt [TimeSpan]::FromMinutes($TimeoutMinutes)) {
        Get-Process -Name WindowsSandbox, WindowsSandboxClient, WindowsSandboxRemoteSession -ErrorAction SilentlyContinue | Stop-Process -Force
        throw "No result after $TimeoutMinutes minutes; see $Out\probe.log"
    }
    Start-Sleep -Seconds 5
}
# The probe shuts the Sandbox down itself; make sure it's gone.
$closing = Get-Date
while (Get-Process -Name WindowsSandbox, WindowsSandboxClient, WindowsSandboxRemoteSession -ErrorAction SilentlyContinue) {
    if ((Get-Date) - $closing -gt [TimeSpan]::FromSeconds(60)) {
        Get-Process -Name WindowsSandbox, WindowsSandboxClient, WindowsSandboxRemoteSession -ErrorAction SilentlyContinue | Stop-Process -Force
        break
    }
    Start-Sleep -Seconds 2
}
Remove-Item -LiteralPath $wsb -Force
$reportPath = Join-Path $Out 'results.json'
Get-Content -LiteralPath $reportPath
if (($Checks | ForEach-Object { $_ -split ',' }) -contains 'arrangements') {
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.error -or $report.arrangements.passed -ne $true) { throw 'Arrangements Sandbox check failed; see results.json' }
}
