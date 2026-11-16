# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors
param([switch]$SnapMedium)
$ErrorActionPreference='Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or !(Test-Path 'C:\chippi\Chippi.Elevated.exe')) { throw 'Windows Sandbox only.' }
if ($SnapMedium) {
    try { & C:\chippi\Chippi.exe --sandbox } finally { Set-Content C:\results\done.txt 'medium finished' }
    exit $LASTEXITCODE
}
Start-Transcript C:\results\guest.log
try {
    # All installation is inside this disposable guest, never on the host.
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    Install-PackageProvider NuGet -Force -Scope CurrentUser | Out-Null
    Install-Module Microsoft.WinGet.Client -Repository PSGallery -Force -Scope CurrentUser
    Import-Module Microsoft.WinGet.Client
    Repair-WinGetPackageManager -Force -Latest
    $winget=(Get-AppxPackage Microsoft.DesktopAppInstaller).InstallLocation + '\winget.exe'
    $versions=@(& $winget show --id 7zip.7zip --exact --source winget --versions --accept-source-agreements --disable-interactivity |
        ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+(\.\d+)+$' })
    if ($versions.Count -lt 2) { throw 'Need two published 7-Zip versions for the upgrade fixture.' }
    & $winget install --id 7zip.7zip --exact --source winget --scope machine --version $versions[1] --silent --accept-source-agreements --accept-package-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0) { throw 'Fixture installation failed.' }
    $before=(& $winget list --id 7zip.7zip --exact --scope machine --accept-source-agreements --disable-interactivity | Out-String)
    & C:\input-probe\snap-medium.ps1
    $report=Get-Content C:\results\helper.json -Raw | ConvertFrom-Json
    if (!$report.passed) { throw ('Helper check failed: '+($report | ConvertTo-Json -Depth 8 -Compress)) }
    $after=(& $winget list --id 7zip.7zip --exact --scope machine --accept-source-agreements --disable-interactivity | Out-String)
    if ($before -notmatch [regex]::Escape($versions[1]) -or $after -notmatch [regex]::Escape($versions[0])) { throw 'Installed version did not advance as expected.' }
    @{passed=$true;helper=$report;before=$before;after=$after;prompt='Sandbox UAC behavior is not proof of owner consent/decline'} |
        ConvertTo-Json -Depth 10 | Set-Content C:\results\results.json
}
catch { @{passed=$false;error=$_.ToString()} | ConvertTo-Json | Set-Content C:\results\results.json }
finally {
    Stop-Transcript
    Set-Content C:\results\finished.txt 'finished'
    shutdown.exe /s /t 0
}
