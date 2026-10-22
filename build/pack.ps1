# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,
    [ValidateSet('win-x64', 'win-arm64')]
    [string[]] $Runtime = @('win-x64', 'win-arm64')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Velopack tool restore failed. Stop and report; do not replace the packaging tool.' }

    foreach ($rid in $Runtime) {
        $staging = Join-Path $PSScriptRoot "artifacts/staging/$([Guid]::NewGuid().ToString('N'))/$rid"
        $output = Join-Path $PSScriptRoot "artifacts/releases/$Version/$rid"
        dotnet publish src/Chippi.App/Chippi.App.csproj "-p:PublishProfile=$rid" "-p:Version=$Version" -o $staging
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid." }
        foreach ($required in 'Chippi.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
            if (!(Test-Path -LiteralPath (Join-Path $staging $required))) { throw "Missing published file: $required" }
        }
        # Offline packaging: no update feed, framework downloads, signing or upload commands.
        dotnet tool run vpk -- pack --packId Chippi --packVersion $Version --packDir $staging --mainExe Chippi.exe --runtime $rid --channel $rid --outputDir $output --packAuthors 'Chippi contributors' --packTitle Chippi --skip-updates --yes
        if ($LASTEXITCODE -ne 0) { throw "Velopack failed for $rid. Stop and report; do not switch tools." }
        $setup = @(Get-ChildItem -LiteralPath $output -Filter '*Setup.exe')
        $portable = @(Get-ChildItem -LiteralPath $output -Filter '*Portable.zip')
        if ($setup.Count -eq 0 -or $portable.Count -eq 0) { throw "Velopack did not produce Setup.exe and portable ZIP for $rid." }
        Get-ChildItem -LiteralPath $output -File | ForEach-Object {
            [pscustomobject]@{ Runtime = $rid; Name = $_.Name; Bytes = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        } | Export-Csv -LiteralPath (Join-Path $output 'artifacts.csv') -NoTypeInformation
        Write-Output "Packages: $output"
    }
}
finally { Pop-Location }
