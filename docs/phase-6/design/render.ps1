# Renders each board of actions-shortcuts.html and wave-1.html to shots\<board>-<theme>.png at 2x with headless Edge,
# dark and light. Each board reports its own size (body data-size), so edits never leave stale sizes here.
# Uses its own throwaway Edge profile, so it doesn't touch the everyday one.
param([string[]]$Only)
$ErrorActionPreference = 'Continue' # Edge reports each write on stderr
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$out = Join-Path $PSScriptRoot 'shots'
$profileDir = Join-Path $env:TEMP 'chippi-mockup-edge'
New-Item -ItemType Directory -Force $out | Out-Null

$pages = [ordered]@{
    'actions-shortcuts.html' = 'sheet-actions', 'sheet-menus', 'sheet-snap-states', 'settings', 'settings-narrow', 'sheet-shortcut-states'
    'wave-1.html'            = 'settings-general', 'settings-radial', 'settings-mouse', 'settings-keyboard', 'settings-keyboard-narrow',
                               'sheet-settings-states', 'sheet-bar'
}
foreach ($file in $pages.Keys) {
    $page = ([Uri](Join-Path $PSScriptRoot $file)).AbsoluteUri
    foreach ($b in $pages[$file]) {
        if ($Only -and $b -notin $Only) { continue }
        foreach ($theme in 'dark', 'light') {
            $url = "$page`?board=$b&theme=$theme"
            $dom = & $edge --headless=new --disable-gpu "--user-data-dir=$profileDir" --window-size=3000,3000 --virtual-time-budget=3000 --dump-dom $url 2>$null | Out-String
            if ($dom -notmatch 'data-size="(\d+),(\d+)"') { "{0}-{1}: no size" -f $b, $theme; continue }
            $png = Join-Path $out "$b-$theme.png"
            & $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=2 `
                "--user-data-dir=$profileDir" "--window-size=$($Matches[1]),$($Matches[2])" "--screenshot=$png" `
                --virtual-time-budget=3000 $url 2>$null | Out-Null
            "{0}-{1}: {2}x{3}" -f $b, $theme, $Matches[1], $Matches[2]
        }
    }
}
