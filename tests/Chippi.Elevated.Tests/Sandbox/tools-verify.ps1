# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors
# After the probe: what actually happened to tools-fixture.ps1's things, read elevated. Writes C:\results\results.json.
param([string]$ProbeError)
$ErrorActionPreference='Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'Windows Sandbox only.' }
$failures=New-Object System.Collections.Generic.List[string]
function Expect([bool]$ok, [string]$what) { if (!$ok) { $failures.Add($what) } }
$fixture=Get-Content C:\results\fixture.json -Raw | ConvertFrom-Json
$probe=if (Test-Path C:\results\tools.json) { [string](Get-Content C:\results\tools.json -Raw) | ConvertFrom-Json } else { $null }
if ($ProbeError) { $failures.Add("probe: $ProbeError") }
if ($null -eq $probe) { $failures.Add('probe: no tools.json') } elseif (!$probe.passed) { foreach ($f in $probe.failures) { $failures.Add($f) } }

# Ports
$held=@(Get-NetTCPConnection -LocalPort 47011,47012,47013 -State Listen -ErrorAction SilentlyContinue)
Expect ($held.Count -eq 0) 'verify: a fixture port is still listening'
Expect ($null -eq (Get-Process -Id $fixture.ports.window.pid -ErrorAction SilentlyContinue)) 'verify: the window listener is still running'
Expect ($null -eq (Get-Process -Id $fixture.ports.hidden.pid -ErrorAction SilentlyContinue)) 'verify: the windowless listener is still running'
Expect ((Get-Service ChippiFixtureSvc).Status -eq 'Stopped') 'verify: the fixture service is not stopped'

# Cleaner
$c=$fixture.cleaner
foreach ($gone in 'windowsTempOld','windowsTempNested','updateOld','deliveryOld','dumpOld','userTempOld') { Expect (!(Test-Path -LiteralPath $c.$gone)) "verify: $gone still there" }
foreach ($kept in 'windowsTempNew','windowsTempInUse','keep','userTempNew') { Expect (Test-Path -LiteralPath $c.$kept) "verify: $kept was deleted" }
$link=Get-Item -LiteralPath $c.windowsTempLink -Force -ErrorAction SilentlyContinue
$cleanerLink=if ($null -eq $link) { 'removed' } elseif ($link.LinkType) { 'kept as link' } else { 'not a link' }

# Uninstaller
$pf=$env:ProgramFiles
$uninstall='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
Expect (!(Test-Path "$uninstall\ChippiFixtureNotes")) 'verify: Notes entry still there'
Expect (!(Test-Path "$uninstall\ChippiFixtureRelaunch")) 'verify: Relaunch entry still there'
Expect (Test-Path "$uninstall\ChippiFixtureWritable") 'verify: the refused app''s entry went'
Expect (Test-Path "$pf\Fixture Works\Writable\app.exe") 'verify: the refused app''s files went'
Expect (!(Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ChippiFixtureUser')) 'verify: user app entry still there'
Expect (!(Test-Path "$pf\Fixture Labs\Notes")) 'verify: Notes install folder still there'
Expect (Test-Path 'C:\ChippiKeepNotes\keep.txt') 'verify: the junction target in the Notes folder was deleted'
Expect (!(Test-Path 'HKLM:\SOFTWARE\Fixture Labs\Fixture Notes')) 'verify: the Fixture Notes key still there'
Expect (Test-Path 'HKLM:\SOFTWARE\Fixture Labs\Notes') 'verify: a key the record doesn''t name was removed'
Expect (!(Test-Path 'C:\ProgramData\Fixture Labs\Notes')) 'verify: Notes ProgramData folder still there'
Expect (!(Test-Path "$env:APPDATA\Fixture Labs\Notes")) 'verify: Notes AppData folder still there'
Expect (!(Test-Path "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Fixture Notes.lnk")) 'verify: Notes shortcut still there'
Expect (Test-Path 'C:\ProgramData\Mozilla\shared.ini') 'verify: the shared Mozilla folder was removed'
Expect (Test-Path "$env:APPDATA\Mozilla\Thunderbird\profiles.ini") 'verify: Thunderbird''s profile was removed'
Expect (Test-Path "$pf\Mozilla Thunderbird\app.exe") 'verify: Thunderbird''s install was removed'
Expect (!(Test-Path "$env:APPDATA\Fixture User Tool")) 'verify: the user app''s leftover still there'
$backups=@(Get-ChildItem C:\results\uninstaller\backup -Recurse -Filter *.reg -ErrorAction SilentlyContinue | ForEach-Object { [string]$_.Name })
$helperBackups=@(Get-ChildItem "$env:LOCALAPPDATA\Chippi\leftovers-backup" -Recurse -Filter *.reg -ErrorAction SilentlyContinue | ForEach-Object { [string]$_.FullName })
# Two machine keys (Fixture Notes, Mozilla Firefox) go through the helper, which exports each to its own fixed folder first.
Expect ($helperBackups.Count -ge 2) 'verify: the helper exported fewer than two keys'
$bin=@((New-Object -ComObject Shell.Application).NameSpace(10).Items() | ForEach-Object { [string]$_.Name })
Expect ($bin.Count -ge 1) 'verify: nothing in the Recycle Bin'
$notes=@(Get-ChildItem C:\results -Filter 'uninstall-*.txt' | ForEach-Object { @{ name=$_.Name; text=[string](Get-Content -LiteralPath $_.FullName -Raw) } })
Expect (@($notes | Where-Object { $_.name -eq 'uninstall-ChippiFixtureRelaunch-child.txt' }).Count -eq 1) 'verify: the relaunched uninstaller copy never ran'
$records=@(Get-ChildItem 'HKLM:\SOFTWARE\Chippi\Elevated\UninstallRecords' -ErrorAction SilentlyContinue | ForEach-Object { [string]$_.PSChildName })

Stop-Process -Id $fixture.holderPid -Force -ErrorAction SilentlyContinue
$guestLabel=[string]$fixture.guestLabel
@{ passed=($failures.Count -eq 0); failures=$failures; probe=$probe; cleanerLink=$cleanerLink; recycleBin=$bin; userBackups=$backups; helperBackups=$helperBackups
   helperRecords=$records; guestLabel=$guestLabel; uninstallerEnvironment=$notes; prompt='Sandbox UAC behavior is not proof of owner consent/decline' } |
    ConvertTo-Json -Depth 12 | Set-Content C:\results\results.json
