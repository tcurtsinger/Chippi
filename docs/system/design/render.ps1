# Renders each board of index.html to shots\<name>.png at 2x with headless Edge.
# Each board reports its own size (body data-size), so edits never leave stale sizes here.
# Uses its own throwaway Edge profile, so it doesn't touch the everyday one.
param([string[]]$Only)
$ErrorActionPreference = 'Continue' # Edge reports each write on stderr
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$page = ([Uri](Join-Path $PSScriptRoot 'index.html')).AbsoluteUri
$out = Join-Path $PSScriptRoot 'shots'
$profileDir = Join-Path $env:TEMP 'chippi-mockup-edge'
# Without these, a first-run or restore bubble can blank the bottom-right corner of a wide shot.
$quiet = "--no-first-run", "--no-default-browser-check", "--hide-crash-restore-bubble", "--disable-notifications"
New-Item -ItemType Directory -Force $out | Out-Null

$boards = [ordered]@{
    'overviews-dark' = 'board=overviews&theme=dark'; 'overviews-light' = 'board=overviews&theme=light'
    'details-dark' = 'board=details&theme=dark'; 'details-light' = 'board=details&theme=light'
    'settings-dark' = 'board=settings&theme=dark'; 'settings-light' = 'board=settings&theme=light'
    'settings-laptop-dark' = 'board=settings&theme=dark&machine=laptop'
}
foreach ($b in $boards.Keys) {
    if ($Only -and $b -notin $Only) { continue }
    $url = "$page`?$($boards[$b])"
    $dom = & $edge --headless=new --disable-gpu "--user-data-dir=$profileDir" --window-size=3000,2000 --virtual-time-budget=3000 --dump-dom $url 2>$null | Out-String
    if ($dom -notmatch 'data-size="(\d+),(\d+)"') { "{0}: no size" -f $b; continue }
    $png = Join-Path $out "$b.png"
    & $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=2 @quiet `
        "--user-data-dir=$profileDir" "--window-size=$($Matches[1]),$($Matches[2])" "--screenshot=$png" `
        --virtual-time-budget=3000 $url 2>$null | Out-Null
    "{0}: {1}x{2}" -f $b, $Matches[1], $Matches[2]
}
