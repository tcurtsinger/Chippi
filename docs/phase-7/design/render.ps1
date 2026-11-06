# Renders each board of surfaces.html to shots\<board>-<theme>.png at 2x with headless Edge, dark and light.
# Each board reports its own size (body data-size). Uses its own throwaway Edge profile.
# Phase 7's mockups are SerenityOS designs in designs/ at the repo root; `serenity design shot` renders those.
param([string[]]$Only)
$ErrorActionPreference = 'Continue' # Edge reports each write on stderr
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$out = Join-Path $PSScriptRoot 'shots'
$profileDir = Join-Path $env:TEMP 'chippi-mockup-edge'
New-Item -ItemType Directory -Force $out | Out-Null

$pages = [ordered]@{
    'surfaces.html' = 'sheet-capture', 'sheet-results', 'sheet-overlay', 'sheet-bar', 'sheet-displays', 'sheet-actions', 'sheet-search', 'settings'
}
foreach ($file in $pages.Keys) {
    $page = ([Uri](Join-Path $PSScriptRoot $file)).AbsoluteUri
    $prefix = ''
    foreach ($b in $pages[$file]) {
        if ($Only -and "$prefix$b" -notin $Only) { continue }
        foreach ($theme in 'dark', 'light') {
            $url = "$page`?board=$b&theme=$theme"
            $dom = & $edge --headless=new --disable-gpu "--user-data-dir=$profileDir" --window-size=3000,3000 --virtual-time-budget=3000 --dump-dom $url 2>$null | Out-String
            if ($dom -notmatch 'data-size="(\d+),(\d+)"') { "{0}{1}-{2}: no size" -f $prefix, $b, $theme; continue }
            $png = Join-Path $out "$prefix$b-$theme.png"
            & $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=2 `
                "--user-data-dir=$profileDir" "--window-size=$($Matches[1]),$($Matches[2])" "--screenshot=$png" `
                --virtual-time-budget=3000 $url 2>$null | Out-Null
            "{0}{1}-{2}: {3}x{4}" -f $prefix, $b, $theme, $Matches[1], $Matches[2]
        }
    }
}
