# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string[]] $Runtime = @('win-x64', 'win-arm64')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $PSScriptRoot 'vendor/soundboard-tools.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$cache = Join-Path $PSScriptRoot 'artifacts/vendor/soundboard'
New-Item -ItemType Directory -Force -Path $cache | Out-Null

# All addresses and digests are release pins, not latest links. No helper is executed or installed.
foreach ($rid in $Runtime) {
    $destination = Join-Path $PSScriptRoot "artifacts/soundboard/$rid"
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    foreach ($property in $manifest.runtimes.$rid.PSObject.Properties) {
        $name = $property.Name
        $pin = $property.Value
        $archive = Join-Path $cache $pin.archive
        $valid = (Test-Path -LiteralPath $archive) -and (Get-Item -LiteralPath $archive).Length -eq $pin.archiveBytes -and
            (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -eq $pin.archiveSHA256
        if (!$valid) {
            Invoke-WebRequest -Uri $pin.url -OutFile $archive
            if ((Get-Item -LiteralPath $archive).Length -ne $pin.archiveBytes -or
                (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $pin.archiveSHA256) {
                throw "Soundboard dependency failed verification: $name ($rid). Do not update the pin to accept it."
            }
        }
        $target = Join-Path $destination $name
        if ($pin.entry) {
            $zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
            try {
                $entry = $zip.GetEntry($pin.entry)
                if (!$entry) { throw "Missing pinned archive entry: $($pin.entry)" }
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            }
            finally { $zip.Dispose() }
        }
        else { Copy-Item -LiteralPath $archive -Destination $target -Force }
        if ((Get-Item -LiteralPath $target).Length -ne $pin.bytes -or
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $pin.sha256) {
            throw "Soundboard executable failed verification: $name ($rid)"
        }
    }
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $destination 'tools.json') -Force
    $licenses = Join-Path $destination 'licenses'
    New-Item -ItemType Directory -Force -Path $licenses | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'vendor/soundboard-licenses') -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenses $_.Name) -Force
    }
    Write-Output "Soundboard tools verified: $destination"
}
