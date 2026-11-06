# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# The capture results check (Color picker, Text and QR on the capture layer), run by probe.ps1 inside the Sandbox with
# [Probe] loaded. The shortcuts are bound in the settings: Ctrl+Alt+1 Pick color, 2 Pick and review color, 3 Copy text
# from screen, 4 Extract text, 5 Scan QR code. The Sandbox's own clipboard is read (redirection is off). The fixture
# (results-fixture.ps1) has a #FF2050 panel, the text "Quarterly review of the numbers" and a QR of
# https://example.com/chippi-check. Returns what it found.

$ErrorActionPreference = 'Stop'
$results = 'C:\results'
$found = [ordered]@{}
function Note([string]$text) { Add-Content -LiteralPath "$results\probe.log" -Value ('{0:HH:mm:ss.fff} {1}' -f (Get-Date), $text) }
function Tap([uint16]$vk) { [Probe]::Key($vk, $true); [Probe]::Spin(25); [Probe]::Key($vk, $false); [Probe]::Spin(25) }
function Chord([uint16[]]$keys) { foreach ($k in $keys) { [Probe]::Key($k, $true); [Probe]::Spin(15) }; [array]::Reverse($keys); foreach ($k in $keys) { [Probe]::Key($k, $false); [Probe]::Spin(15) } }
function Starter([int]$digit) { Chord @(0xA2, 0xA4, [uint16](0x30 + $digit)) }
function Click([int]$x, [int]$y) { [Probe]::MoveTo($x, $y); Start-Sleep -Milliseconds 120; [Probe]::Left($true); [Probe]::Spin(40); [Probe]::Left($false) }
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -Namespace ChippiCheck -Name Clip -MemberDefinition '[DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();'
function Shot([string]$name) {
    $v = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $b = New-Object System.Drawing.Bitmap $v.Width, $v.Height
    $g = [System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($v.X, $v.Y, 0, 0, $b.Size); $g.Dispose()
    $b.Save("$results\results-$name.png"); $b.Dispose()
}
function ClipText { try { [string](Get-Clipboard -Raw -ErrorAction Stop) } catch { '' } }
# Press, drag and release as a hand does ($Ctrl: the fast path, the release finishes). Returns Stopwatch ms at the release.
function Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2, [switch]$Ctrl) {
    [Probe]::MoveTo($x1, $y1); Start-Sleep -Milliseconds 120
    if ($Ctrl) { [Probe]::Key(0xA2, $true) }
    [Probe]::Left($true); Start-Sleep -Milliseconds 60
    [Probe]::Glide($x1, $y1, $x2, $y2, 250); Start-Sleep -Milliseconds 60
    $script:release = [Diagnostics.Stopwatch]::StartNew()
    [Probe]::Left($false)
    if ($Ctrl) { [Probe]::Key(0xA2, $false) }
}
# Milliseconds from $script:release until the clipboard's sequence number moves past $from (polled every 2 ms), or -1.
function CopyTime([uint32]$from, [int]$limit = 8000) {
    while ($script:release.ElapsedMilliseconds -lt $limit) {
        if ([ChippiCheck.Clip]::GetClipboardSequenceNumber() -ne $from) { return [math]::Round($script:release.Elapsed.TotalMilliseconds, 1) }
        Start-Sleep -Milliseconds 2
    }
    return -1
}

$ReselectX = 1780; $ReselectY = 903   # the Reselect button in the Text page, from the shots
$fixture = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\results-fixture.ps1' -PassThru
Start-Sleep -Seconds 5

$settingsDir = Join-Path $env:APPDATA 'Chippi'
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
$keys = { param($d) '{"modifiers":3,"key":' + (0x30 + $d) + '}' }
$settings = '{"version":1,"modules":{"color-picker":{"copyFormat":"HEX","pickKeys":' + (& $keys 1) + ',"reviewKeys":' + (& $keys 2) + '},' +
    '"screen-text":{"joinLines":false,"copyKeys":' + (& $keys 3) + ',"extractKeys":' + (& $keys 4) + ',"scanKeys":' + (& $keys 5) + '}},' +
    '"features":{"preset":"custom","installed":["color-picker","screen-text"],"capabilitiesUsed":[]}}'
Set-Content -LiteralPath (Join-Path $settingsDir 'settings.json') -Encoding ASCII -Value $settings
$env:CHIPPI_CAPTURE_TIMINGS = "$results\capture-timings.txt"
$chippi = Start-Process -FilePath 'C:\chippi\Chippi.exe' -PassThru
Start-Sleep -Seconds 20
$chippi.Refresh()
if ($chippi.HasExited) { throw "Chippi exited ($($chippi.ExitCode))" }
Shot 'start'

function Fixture { Click 1000 650; Start-Sleep -Milliseconds 300 }   # a spot of the fixture's white, so the hand-back has somewhere to go

# --- Color picker: Pick color, 5 times (the first is cold), each timed from the click to the clipboard.
$pick = @()
foreach ($i in 1..5) {
    Fixture
    $before = [ChippiCheck.Clip]::GetClipboardSequenceNumber()
    Set-Clipboard -Value 'untouched'; $before = [ChippiCheck.Clip]::GetClipboardSequenceNumber()
    Starter 1; Start-Sleep -Milliseconds 700
    [Probe]::MoveTo(200, 300); Start-Sleep -Milliseconds 200
    if ($i -eq 1) { Shot 'pick-frozen' }
    $script:release = [Diagnostics.Stopwatch]::StartNew()
    [Probe]::Left($true); [Probe]::Spin(40); [Probe]::Left($false)
    $ms = CopyTime $before
    Start-Sleep -Milliseconds 300
    if ($i -eq 1) { Shot 'pick-bar' }
    $pick += [ordered]@{ copyMs = $ms; clipboard = (ClipText).Trim(); foreground = [Probe]::Foreground() }
    Start-Sleep -Milliseconds 1500
}
$found.pick = $pick
# Esc cancels and leaves the clipboard alone.
Fixture; Set-Clipboard -Value 'untouched'
Starter 1; Start-Sleep -Milliseconds 700; Tap 0x1B; Start-Sleep -Milliseconds 700
$found.pickEsc = [ordered]@{ clipboard = (ClipText).Trim(); foreground = [Probe]::Foreground() }
# A right click cancels too.
Fixture; Set-Clipboard -Value 'untouched'
Starter 1; Start-Sleep -Milliseconds 700; [Probe]::MoveTo(200, 300); Start-Sleep -Milliseconds 100; [Probe]::Right($true); [Probe]::Right($false); Start-Sleep -Milliseconds 700
$found.pickRightClick = [ordered]@{ clipboard = (ClipText).Trim(); foreground = [Probe]::Foreground() }
# Pick and review: copies nothing, opens the Color page with the picked color.
Fixture; Set-Clipboard -Value 'untouched'
Shot 'review-0'
Starter 2; Start-Sleep -Milliseconds 700; Shot 'review-1'; Click 200 300; Start-Sleep -Milliseconds 1200
Shot 'color-page'
$found.pickReview = [ordered]@{ clipboard = (ClipText).Trim(); foreground = [Probe]::Foreground() }
Click 1000 650; Start-Sleep -Milliseconds 800; Shot 'review-2'
$found.afterReviewForeground = [Probe]::Foreground()

# --- Copy text from screen: Ctrl drag over the text, 5 times; then once with Enter.
$text = @()
foreach ($i in 1..5) {
    Fixture
    Set-Clipboard -Value 'untouched'; $before = [ChippiCheck.Clip]::GetClipboardSequenceNumber()
    Starter 3; Start-Sleep -Milliseconds 700
    Drag 360 95 900 175 -Ctrl
    $ms = CopyTime $before
    Start-Sleep -Milliseconds 300
    if ($i -eq 1) { Shot 'text-bar' }
    $text += [ordered]@{ copyMs = $ms; clipboard = (ClipText).Trim(); foreground = [Probe]::Foreground() }
    Start-Sleep -Milliseconds 1500
}
$found.copyText = $text
Fixture; Set-Clipboard -Value 'untouched'; $before = [ChippiCheck.Clip]::GetClipboardSequenceNumber()
Starter 3; Start-Sleep -Milliseconds 700
Drag 360 95 900 175
Start-Sleep -Milliseconds 300; Shot 'text-selected'
$script:release = [Diagnostics.Stopwatch]::StartNew(); Tap 0x0D
$found.copyTextEnter = [ordered]@{ copyMs = (CopyTime $before); clipboard = (ClipText).Trim() }
Start-Sleep -Milliseconds 1500
# A region with no text: the bar says so and the clipboard stays.
Fixture; Set-Clipboard -Value 'untouched'
Starter 3; Start-Sleep -Milliseconds 700
Drag 1000 400 1090 480 -Ctrl
Start-Sleep -Milliseconds 1200; Shot 'text-none'
$found.copyTextBlank = [ordered]@{ clipboard = (ClipText).Trim() }
Start-Sleep -Milliseconds 1500
# Esc cancels.
Fixture; Set-Clipboard -Value 'untouched'
Starter 3; Start-Sleep -Milliseconds 700; Tap 0x1B; Start-Sleep -Milliseconds 700
$found.copyTextEsc = [ordered]@{ clipboard = (ClipText).Trim() }

# Esc after a 5 x 6 px drag (past the drag threshold, under 8 px): the capture must end, then a new one must start.
Fixture; Set-Clipboard -Value 'untouched'
Starter 3; Start-Sleep -Milliseconds 700
Drag 400 300 405 306
Start-Sleep -Milliseconds 700; Shot 'tiny-drag'
$found.tinyDrag = [ordered]@{ foregroundAfterDrag = [Probe]::Foreground() }
Tap 0x1B; Start-Sleep -Milliseconds 500
$found.tinyDrag.foregroundAfterEsc = [Probe]::Foreground()
Starter 1; Start-Sleep -Milliseconds 700
[Probe]::MoveTo(200, 300); Start-Sleep -Milliseconds 100; [Probe]::Left($true); [Probe]::Spin(40); [Probe]::Left($false); Start-Sleep -Milliseconds 400
$found.tinyDrag.nextCaptureCopied = (ClipText).Trim()

# --- Extract text: opens the Text page without copying.
Fixture; Set-Clipboard -Value 'untouched'
Starter 4; Start-Sleep -Milliseconds 700
Drag 360 95 900 175 -Ctrl
Start-Sleep -Milliseconds 1500; Shot 'text-page'
$found.extract = [ordered]@{ clipboard = (ClipText).Trim() }
# Reselect, then Esc: the Text page must not stay on "Reading text". The Reselect button is found by its place in the shot.
Click $ReselectX $ReselectY; Start-Sleep -Milliseconds 800; Shot 'reselect-selector'
$found.reselect = [ordered]@{ foregroundAfterReselect = [Probe]::Foreground() }
Tap 0x1B; Start-Sleep -Milliseconds 800; Shot 'reselect-esc'
$found.reselect.foregroundAfterEsc = [Probe]::Foreground()
Click 1000 650; Start-Sleep -Milliseconds 800

# --- Scan QR: opens the QR page; the decoded address is read from the shot.
$qr = @()
foreach ($i in 1..3) {
    Fixture
    Starter 5; Start-Sleep -Milliseconds 700
    Drag 360 290 710 640 -Ctrl
    Start-Sleep -Milliseconds 1500
    Shot "qr-page$i"
    $qr += [ordered]@{ foreground = [Probe]::Foreground() }
    Click 1000 650; Start-Sleep -Milliseconds 800
}
$found.scanQr = $qr
# A damaged code: its finder corners stay, a square of data is gone.
Fixture
Starter 5; Start-Sleep -Milliseconds 700
Drag 730 290 1090 640 -Ctrl
Start-Sleep -Milliseconds 1500; Shot 'qr-damaged'
Click 1000 650; Start-Sleep -Milliseconds 600
# The QR page's results are gone after it closes: reopen the tile by Scan QR on a blank region and read what it shows.
# A region with no code.
Fixture
Starter 5; Start-Sleep -Milliseconds 700
Drag 1000 400 1090 480 -Ctrl
Start-Sleep -Milliseconds 1500; Shot 'qr-none'

if (Test-Path "$results\capture-timings.txt") { $found.timings = @(Get-Content -LiteralPath "$results\capture-timings.txt" | ForEach-Object { [string]$_ }) }
$logs = Join-Path $env:LOCALAPPDATA 'Chippi\logs'
if (Test-Path $logs) { Copy-Item -Path $logs -Destination "$results\chippi-logs" -Recurse -ErrorAction SilentlyContinue }
Stop-Process -Id $chippi.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $fixture.Id -Force -ErrorAction SilentlyContinue
return $found
