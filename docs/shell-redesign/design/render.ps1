# Renders a mock page (index.html by default) to shots\<Name>.png at 2x with headless Edge, in its own throwaway profile.
param([string]$Page = 'index.html', [string]$Name = 'looks', [string]$Query = '')
$ErrorActionPreference = 'Continue' # Edge reports each write on stderr
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$page = ([Uri](Join-Path $PSScriptRoot $Page)).AbsoluteUri + $Query
$out = Join-Path $PSScriptRoot 'shots'
$profileDir = Join-Path $env:TEMP 'chippi-mockup-edge'
New-Item -ItemType Directory -Force $out | Out-Null
$dom = & $edge --headless=new --disable-gpu "--user-data-dir=$profileDir" --window-size=2000,2400 --virtual-time-budget=5000 --dump-dom $page 2>$null | Out-String
if ($dom -notmatch 'data-size="(\d+),(\d+)"') { 'no size'; exit 1 }
$png = Join-Path $out "$Name.png"
& $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=2 `
    "--user-data-dir=$profileDir" "--window-size=$($Matches[1]),$($Matches[2])" "--screenshot=$png" `
    --virtual-time-budget=5000 $page 2>$null | Out-Null
"{0}: {1}x{2}" -f $Name, $Matches[1], $Matches[2]
