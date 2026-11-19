# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors
param([switch]$AppUpdates,[switch]$Tools)
$ErrorActionPreference='Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or !(Test-Path 'C:\source-app\Chippi.Elevated.exe')) { throw 'Windows Sandbox only.' }
Start-Transcript C:\results-out\guest.log
try {
    $rootAcl=Get-Acl -LiteralPath 'C:\'
    @($rootAcl.Access | ForEach-Object { @{identity=$_.IdentityReference.Value;rights=$_.FileSystemRights.ToString();inheritance=$_.InheritanceFlags.ToString();propagation=$_.PropagationFlags.ToString()} }) |
        ConvertTo-Json -Depth 3 | Set-Content C:\results-out\root-access.json
    # Restricted tokens must use real guest paths, not redirected host folders.
    Copy-Item -LiteralPath C:\source-app -Destination C:\chippi -Recurse
    Copy-Item -LiteralPath C:\source-probe -Destination C:\probe -Recurse
    New-Item -ItemType Directory C:\results -Force | Out-Null
    icacls.exe C:\chippi /grant '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
    icacls.exe C:\probe /grant '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Guest executable permissions failed.' }
    icacls.exe C:\results /grant '*S-1-5-32-545:(OI)(CI)M' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Guest result permissions failed.' }
    & C:\probe\medium.ps1 -Mode '--preflight'
    & C:\probe\medium.ps1 -Mode '--handshake'
    if ($Tools) {
        # Port manager, Cleaner and Uninstaller on throwaway fixtures; no network, nothing installed.
        & C:\probe\tools-fixture.ps1
        $probeError=$null
        try { & C:\probe\medium.ps1 -Mode '--tools' } catch { $probeError=$_.Exception.Message }
        & C:\probe\tools-verify.ps1 -ProbeError $probeError
        return
    }
    # All installation is inside this disposable guest, never on the host.
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    Install-PackageProvider NuGet -Force -Scope CurrentUser | Out-Null
    Install-Module Microsoft.WinGet.Client -Repository PSGallery -Force -Scope CurrentUser
    Import-Module Microsoft.WinGet.Client
    Repair-WinGetPackageManager -Force -Latest
    $winget=(Get-AppxPackage Microsoft.DesktopAppInstaller).InstallLocation + '\winget.exe'
    & C:\probe\medium.ps1 -Mode '--guard'
    $versions=@(& $winget show --id 7zip.7zip --exact --source winget --versions --accept-source-agreements --disable-interactivity |
        ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+(\.\d+)+$' })
    if ($versions.Count -lt 2) { throw 'Need two published 7-Zip versions for the upgrade fixture.' }
    & $winget install --id 7zip.7zip --exact --source winget --scope machine --version $versions[1] --silent --accept-source-agreements --accept-package-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0) { throw 'Fixture installation failed.' }
    $before=(& $winget list --id 7zip.7zip --exact --source winget --scope machine --accept-source-agreements --disable-interactivity | Out-String)
    $userBefore=$null; $userAfter=$null
    if ($AppUpdates) {
        & $winget install --id SumatraPDF.SumatraPDF --exact --source winget --version 3.6 --scope user --installer-type exe --architecture x64 --silent --accept-source-agreements --accept-package-agreements --disable-interactivity
        if ($LASTEXITCODE -ne 0) { throw 'User fixture installation failed.' }
        $userBefore=(& $winget list --id SumatraPDF.SumatraPDF --exact --source winget --scope user --accept-source-agreements --disable-interactivity | Out-String)
    }
    if ($AppUpdates) { & C:\probe\medium.ps1 -Mode '--app-updates' }
    else { & C:\probe\medium.ps1 -Mode '--sandbox' }
    $report=Get-Content C:\results\helper.json -Raw | ConvertFrom-Json
    if (!$report.passed) { throw ('Helper check failed: '+($report | ConvertTo-Json -Depth 8 -Compress)) }
    $exitDeadline=(Get-Date).AddSeconds(10)
    while (@(Get-Process -Name 'Chippi.Elevated' -ErrorAction SilentlyContinue).Count -gt 0 -and (Get-Date) -lt $exitDeadline) { Start-Sleep -Milliseconds 25 }
    if (@(Get-Process -Name 'Chippi.Elevated' -ErrorAction SilentlyContinue).Count -gt 0) { throw 'Helper did not exit after the completed batch.' }
    $after=(& $winget list --id 7zip.7zip --exact --source winget --scope machine --accept-source-agreements --disable-interactivity | Out-String)
    if ($before -notmatch [regex]::Escape($versions[1]) -or $after -notmatch [regex]::Escape($versions[0])) { throw 'Installed version did not advance as expected.' }
    if ($AppUpdates) {
        $userAfter=(& $winget list --id SumatraPDF.SumatraPDF --exact --source winget --scope user --accept-source-agreements --disable-interactivity | Out-String)
        if ($userBefore -notmatch '3\.6' -or $userAfter -notmatch [regex]::Escape($report.userVersion) -or $userBefore -eq $userAfter) { throw 'User fixture version did not advance as expected.' }
    }
    @{passed=$true;helper=$report;before=$before;after=$after;userBefore=$userBefore;userAfter=$userAfter;prompt='Sandbox UAC behavior is not proof of owner consent/decline'} |
        ConvertTo-Json -Depth 10 | Set-Content C:\results\results.json
}
catch { @{passed=$false;error=$_.Exception.ToString();nativeError=$_.Exception.InnerException.NativeErrorCode} | ConvertTo-Json -Depth 5 | Set-Content C:\results\results.json }
finally {
    if (Test-Path C:\results) { Copy-Item C:\results\* C:\results-out -Force }
    Stop-Transcript
    Set-Content C:\results-out\finished.txt 'finished'
}
