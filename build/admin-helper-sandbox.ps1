# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors
param([Parameter(Mandatory)][string]$App,[Parameter(Mandatory)][string]$Out)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$App=(Resolve-Path -LiteralPath $App).Path
if (!(Test-Path -LiteralPath (Join-Path $App 'Chippi.exe')) -or !(Test-Path -LiteralPath (Join-Path $App 'Chippi.Elevated.exe'))) { throw 'Use the isolated AdminProbe publish folder, not the real app.' }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'build/fullscreen-check.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Fullscreen app or Zoom in front; run later.' }
if (Get-Process -Name WindowsSandbox,WindowsSandboxClient,WindowsSandboxRemoteSession -ErrorAction SilentlyContinue) { throw 'Another Sandbox is running.' }
if ((Test-Path -LiteralPath $Out) -and @(Get-ChildItem -LiteralPath $Out -Force).Count -gt 0) { throw 'Use a new empty output folder.' }
New-Item -ItemType Directory -Path $Out -Force | Out-Null
$Out=(Resolve-Path -LiteralPath $Out).Path
$script=(Resolve-Path -LiteralPath (Join-Path $repo 'tests/Chippi.Elevated.Tests/Sandbox')).Path
$launcher=(Resolve-Path -LiteralPath (Join-Path $repo 'tests/Chippi.Input.Tests/Sandbox')).Path
$escape={ param($value) [Security.SecurityElement]::Escape($value) }
$wsb=Join-Path $Out 'admin-helper.wsb'
@"
<Configuration>
  <Networking>Enable</Networking><ClipboardRedirection>Disable</ClipboardRedirection>
  <AudioInput>Disable</AudioInput><VideoInput>Disable</VideoInput><PrinterRedirection>Disable</PrinterRedirection>
  <MappedFolders>
    <MappedFolder><HostFolder>$(& $escape $App)</HostFolder><SandboxFolder>C:\chippi</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$(& $escape $script)</HostFolder><SandboxFolder>C:\probe</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$(& $escape $launcher)</HostFolder><SandboxFolder>C:\input-probe</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$(& $escape $Out)</HostFolder><SandboxFolder>C:\results</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\probe\probe.ps1</Command></LogonCommand>
</Configuration>
"@ | Set-Content -LiteralPath $wsb -Encoding UTF8
Start-Process -FilePath WindowsSandbox.exe -ArgumentList "`"$wsb`"" -WindowStyle Hidden
$deadline=(Get-Date).AddMinutes(20)
while (!(Test-Path -LiteralPath (Join-Path $Out 'finished.txt'))) {
    if ((Get-Date) -gt $deadline) { throw 'Sandbox timed out. See guest.log; no host processes were terminated.' }
    Start-Sleep -Seconds 3
}
$report=Get-Content -LiteralPath (Join-Path $Out 'results.json') -Raw | ConvertFrom-Json
$report | ConvertTo-Json -Depth 10
if (!$report.passed) { throw 'Admin helper Sandbox check failed.' }
