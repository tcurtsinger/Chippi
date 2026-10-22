# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param(
    [string] $OutputRoot = (Join-Path $PSScriptRoot '../artifacts/toast-spike'),
    [ValidatePattern('^\d+\.\d+\.\d+$')][string] $Version = '0.0.1',
    [ValidateSet('win-x64','win-arm64')][string[]] $Runtime = @('win-x64','win-arm64')
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
Push-Location $repo
try {
    dotnet tool restore
    if ($LASTEXITCODE) { throw 'Tool restore failed.' }
    foreach ($route in 'Classic', 'Modern') {
        foreach ($rid in $Runtime) {
            $publish = Join-Path $OutputRoot "$route/$rid/publish"
            $packages = Join-Path $OutputRoot "$route/$rid/packages"
            New-Item -ItemType Directory -Force $publish,$packages | Out-Null
            dotnet publish "build/toast-spike/$route/$route.csproj" -c Release -r $rid --self-contained true -p:PublishSingleFile=false -p:DebugType=None "-p:Version=$Version" -o $publish
            if ($LASTEXITCODE) { throw "Publish failed: $route $rid" }
            Copy-Item -LiteralPath (Join-Path $repo 'LICENSE'),(Join-Path $repo 'THIRD-PARTY-NOTICES.md') -Destination $publish
            # Include upstream notices from every restored package, not only the top-level SDK.
            $assets = Get-Content -LiteralPath "build/toast-spike/$route/obj/project.assets.json" -Raw | ConvertFrom-Json
            foreach ($library in $assets.libraries.PSObject.Properties) {
                if ($library.Value.type -ne 'package') { continue }
                $packageRoot = Join-Path $env:USERPROFILE ".nuget/packages/$($library.Name.ToLowerInvariant())"
                $licenses = @(Get-ChildItem -LiteralPath $packageRoot -File | Where-Object Name -Match 'license|notice|third.party')
                if ($licenses.Count) {
                    $destination = Join-Path $publish "PackageNotices/$($library.Name)"
                    New-Item -ItemType Directory -Force $destination | Out-Null
                    $licenses | Copy-Item -Destination $destination
                }
            }
            'Internal toast spike, not a Chippi release. PackageNotices contains upstream package notices; production integration requires the normal dependency and redistribution review.' | Set-Content (Join-Path $publish 'SPIKE-NOTICES.txt')
            dotnet tool run vpk -- pack --packId "ChippiToast${route}Spike" --packVersion $Version --packDir $publish --mainExe "Chippi.Toast$route.exe" --runtime $rid --channel $rid --outputDir $packages --packTitle "Chippi toast $route spike" --packAuthors 'Chippi contributors' --skip-updates --yes
            if ($LASTEXITCODE) { throw "Velopack failed: $route $rid. Stop; do not replace it." }
        }
    }
    $rows = foreach ($route in 'Classic', 'Modern') {
        foreach ($rid in $Runtime) {
            $publish = Join-Path $OutputRoot "$route/$rid/publish"
            $total = (Get-ChildItem -LiteralPath $publish -Recurse -File | Measure-Object Length -Sum).Sum
            Get-ChildItem -LiteralPath (Join-Path $OutputRoot "$route/$rid/packages") -File | ForEach-Object {
                [pscustomobject]@{ Route=$route; Runtime=$rid; Name=$_.Name; Bytes=$_.Length; SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; PublishedBytes=$total }
            }
        }
    }
    $rows | Export-Csv -LiteralPath (Join-Path $OutputRoot 'sizes.csv') -NoTypeInformation
}
finally { Pop-Location }
