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

    # VB-CABLE ships unmodified (its readme allows copying only as is), pinned by size and SHA-256 in
    # build/vendor/vb-cable.json. Downloaded once into the cache; any mismatch fails the build.
    $pin = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'vendor/vb-cable.json') | ConvertFrom-Json
    $vendorCache = Join-Path $PSScriptRoot 'artifacts/vendor'
    New-Item -ItemType Directory -Force -Path $vendorCache | Out-Null
    $vbZip = Join-Path $vendorCache $pin.file
    $vbOk = { (Test-Path -LiteralPath $vbZip) -and (Get-Item -LiteralPath $vbZip).Length -eq $pin.bytes -and (Get-FileHash -LiteralPath $vbZip -Algorithm SHA256).Hash -eq $pin.sha256 }
    if (!(& $vbOk)) {
        Invoke-WebRequest -Uri $pin.url -OutFile $vbZip -UseBasicParsing
        if (!(& $vbOk)) { throw 'VB-CABLE package does not match build/vendor/vb-cable.json. Stop and report; do not update the pin without review.' }
    }

    foreach ($rid in $Runtime) {
        $staging = Join-Path $PSScriptRoot "artifacts/staging/$([Guid]::NewGuid().ToString('N'))/$rid"
        $output = Join-Path $PSScriptRoot "artifacts/releases/$Version/$rid"
        dotnet publish src/Chippi.App/Chippi.App.csproj "-p:PublishProfile=$rid" "-p:Version=$Version" -o $staging
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid." }
        Copy-Item -LiteralPath $vbZip -Destination (Join-Path $staging "vendor/vb-cable/$($pin.file)")
        foreach ($required in 'Chippi.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'vendor/vb-cable/vb-cable.json', "vendor/vb-cable/$($pin.file)", 'runtimes/win-x64/native/rnnoise.dll', 'runtimes/win-arm64/native/rnnoise.dll') {
            if (!(Test-Path -LiteralPath (Join-Path $staging $required))) { throw "Missing published file: $required" }
        }
        # Offline packaging: no update feed, framework downloads, signing or upload commands.
        dotnet tool run vpk -- pack --packId ChippiApp --packVersion $Version --packDir $staging --mainExe Chippi.exe --runtime $rid --channel $rid --outputDir $output --packAuthors 'Chippi contributors' --packTitle Chippi --skip-updates --yes
        if ($LASTEXITCODE -ne 0) { throw "Velopack failed for $rid. Stop and report; do not switch tools." }
        $setup = @(Get-ChildItem -LiteralPath $output -Filter '*Setup.exe')
        $portable = @(Get-ChildItem -LiteralPath $output -Filter '*Portable.zip')
        if ($setup.Count -eq 0 -or $portable.Count -eq 0) { throw "Velopack did not produce Setup.exe and portable ZIP for $rid." }
        # Materialize before opening the CSV, and exclude a manifest left by a previous run.
        $artifacts = @(Get-ChildItem -LiteralPath $output -File | Where-Object Name -ne 'artifacts.csv' | ForEach-Object {
            [pscustomobject]@{ Runtime = $rid; Name = $_.Name; Bytes = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
        $artifacts | Export-Csv -LiteralPath (Join-Path $output 'artifacts.csv') -NoTypeInformation
        Write-Output "Packages: $output"
    }
}
finally { Pop-Location }
