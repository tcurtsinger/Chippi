# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors
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
    $before=(& $winget list --id 7zip.7zip --exact --scope machine --accept-source-agreements --disable-interactivity | Out-String)
    & C:\probe\medium.ps1 -Mode '--sandbox'
    $report=Get-Content C:\results\helper.json -Raw | ConvertFrom-Json
    if (!$report.passed) { throw ('Helper check failed: '+($report | ConvertTo-Json -Depth 8 -Compress)) }
    $after=(& $winget list --id 7zip.7zip --exact --scope machine --accept-source-agreements --disable-interactivity | Out-String)
    if ($before -notmatch [regex]::Escape($versions[1]) -or $after -notmatch [regex]::Escape($versions[0])) { throw 'Installed version did not advance as expected.' }
    @{passed=$true;helper=$report;before=$before;after=$after;prompt='Sandbox UAC behavior is not proof of owner consent/decline'} |
        ConvertTo-Json -Depth 10 | Set-Content C:\results\results.json
}
catch { @{passed=$false;error=$_.Exception.ToString();nativeError=$_.Exception.InnerException.NativeErrorCode} | ConvertTo-Json -Depth 5 | Set-Content C:\results\results.json }
finally {
    if (Test-Path C:\results) { Copy-Item C:\results\* C:\results-out -Force }
    Stop-Transcript
    Set-Content C:\results-out\finished.txt 'finished'
}
