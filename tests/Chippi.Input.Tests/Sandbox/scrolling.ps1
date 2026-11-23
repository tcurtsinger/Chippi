# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# Scrolling capture's Sandbox check (docs/phase-7/scrolling-capture.md, Tests). Runs only inside Windows Sandbox through run.ps1
# -Checks scrolling, with clipboard redirection and networking off; probe.ps1 dispatches it. The real module runs against:
#   - a local page in Edge (sticky header and footer, fixed sidebar, 2,000 rows each with an index stripe), captured by hand
#     twice (the content alone, then the whole page with its sidebar and scrollbar) at several wheel speeds, one too fast;
#     Auto must be unavailable there (Chromium ignores posted wheel input);
#   - scrolling-fixture.ps1's "Long text" (1,000 striped lines), by hand and with Auto, to its end;
#   - Notepad (1,000 numbered lines) and Windows Settings, with Auto where it is offered. They carry no stripe, so they're
#     recorded (size, end reached, time) but have no row proof.
# Every striped result is read back from its saved PNG by scrolling-verify.ps1: each row once, in order, no gaps. The
# overlay's own windows must not appear in it (the page color is checked between rows). Nothing is read from the clipboard.
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'Run this check only inside Windows Sandbox through run.ps1.' }
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes
Add-Type -Namespace ScrollCheck -Name Dpi -MemberDefinition '[DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);'
[ScrollCheck.Dpi]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScrollCheck
{
    // A wheel from the "hand": SendInput, no Chippi marker, at the pointer.
    public static class Wheel
    {
        [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int Dx, Dy; public uint Data, Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct KeyInput { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Explicit)] struct Union { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
        [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public Union Data; }
        [DllImport("user32.dll")] static extern uint SendInput(uint count, Input[] inputs, int size);

        // Positive notches scroll up (the wheel turned away), negative down: the page moves like a hand's.
        public static void Notches(int notches, int gapMs)
        {
            int count = Math.Abs(notches), delta = notches < 0 ? -120 : 120;
            for (int i = 0; i < count; i++)
            {
                var input = new Input { Type = 0 };
                input.Data.Mouse = new MouseInput { Flags = 0x0800, Data = unchecked((uint)delta) };
                SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input)));
                if (i < count - 1 && gapMs > 0) { var c = Stopwatch.StartNew(); while (c.ElapsedMilliseconds < gapMs) { } }
            }
        }
    }
}
'@
. (Join-Path $PSScriptRoot 'scrolling-verify.ps1')

$results = 'C:\results'
$found = [ordered]@{ passed = $false; verified = [ordered]@{}; timings = [ordered]@{}; recorded = [ordered]@{} }
$headerRgb = @(0x1F, 0x3A, 0x5F); $footerRgb = @(0x3A, 0x1F, 0x5F); $pageRgb = @(255, 255, 255)
function Note([string]$text) { Add-Content -LiteralPath "$results\probe.log" -Value $text }
function Require([bool]$value, [string]$name) { if (-not $value) { throw "Scrolling check failed: $name" }; Note "PASS $name" }
function Aim {
    $front = [Probe]::Foreground()
    if ($front -notmatch 'Scroll page|Long text|Notepad|notepad-lines|Settings|Chippi|Save [Aa]s') { throw "Unexpected foreground before input: $front" }
}
function Tap([uint16]$vk) { Aim; [Probe]::Key($vk, $true); [Probe]::Spin(25); [Probe]::Key($vk, $false); [Probe]::Spin(25) }
function Chord([uint16[]]$keys) {
    Aim
    foreach ($key in $keys) { [Probe]::Key($key, $true); [Probe]::Spin(15) }
    [array]::Reverse($keys)
    foreach ($key in $keys) { [Probe]::Key($key, $false); [Probe]::Spin(15) }
}
function Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2) {
    [Probe]::MoveTo($x1, $y1); [Probe]::Spin(60)
    [Probe]::Left($true); [Probe]::Spin(35); [Probe]::Glide($x1, $y1, $x2, $y2, 160); [Probe]::Left($false)
    $script:released = [Diagnostics.Stopwatch]::StartNew()   # release to bar is timed from here, not from the drag
}
function Shot([string]$name) {
    $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($screen.X, $screen.Y, 0, 0, $bitmap.Size); $graphics.Dispose()
    $bitmap.Save("$results\scrolling-$name.png"); $bitmap.Dispose()
}
function WaitFor([scriptblock]$condition, [string]$description, [int]$milliseconds = 8000) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.ElapsedMilliseconds -lt $milliseconds) { if (& $condition) { return }; Start-Sleep -Milliseconds 50 }
    Note ("TIMEOUT $description foreground=" + [Probe]::Foreground() + " exited=" + $chippi.HasExited)
    Shot 'timeout'
    throw "Timed out: $description"
}
$ui = [System.Windows.Automation.AutomationElement]
$scopeChildren = [System.Windows.Automation.TreeScope]::Children
$scopeDescendants = [System.Windows.Automation.TreeScope]::Descendants
# One element of Chippi's own windows by name. -Button: buttons only. -Text: text only (the bar's line and size).
# Chippi's top-level windows are found first (a shallow search), then searched alone: a descendants search from the desktop
# walks every window's tree, Edge's with renderer accessibility included, which took seconds a call and inflated the timings.
function Find([string]$name, [switch]$Button, [switch]$Text) {
    $processCondition = New-Object System.Windows.Automation.PropertyCondition ($ui::ProcessIdProperty), $chippi.Id
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition ($ui::NameProperty), $name
    # Chippi's windows come and go (the frame, bar, preview and result): one that closes mid-search is skipped, not fatal.
    try { $windows = @($ui::RootElement.FindAll($scopeChildren, $processCondition)) } catch { return $null }
    foreach ($window in $windows) {
        try {
            foreach ($element in $window.FindAll($scopeDescendants, $nameCondition)) {
                $type = $element.Current.ControlType
                if ($Button -and $type -ne [System.Windows.Automation.ControlType]::Button -and $type -ne [System.Windows.Automation.ControlType]::MenuItem) { continue }
                if ($Text -and $type -ne [System.Windows.Automation.ControlType]::Text) { continue }
                return $element
            }
        } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    }
    return $null
}
function Invoke([string]$name) {
    WaitFor { $null -ne (Find $name -Button) } "control $name"
    $done = $false
    for ($try = 0; $try -lt 5 -and -not $done; $try++) {
        try { (Find $name -Button).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); $done = $true }
        catch { Start-Sleep -Milliseconds 100 }
    }
    if (-not $done) { throw "control $name could not be pressed" }
    $script:invoked = [Diagnostics.Stopwatch]::StartNew()   # Done to result is timed from the click
    Start-Sleep -Milliseconds 150
}
# The scrolling bar's words: Scroll, Too fast, Scrolling, End reached, Limit reached; $null when it says nothing.
function BarLine {
    foreach ($words in 'Too fast', 'Scrolling', 'End reached', 'Limit reached', 'Scroll') { if ($null -ne (Find $words -Text)) { return $words } }
    return $null
}
function AutoEnabled { $a = Find 'Auto' -Button; return ($null -ne $a -and $a.Current.IsEnabled) }
function TopWindow([string]$like, [int]$milliseconds = 15000) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.ElapsedMilliseconds -lt $milliseconds) {
        foreach ($w in $ui::RootElement.FindAll($scopeChildren, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($w.Current.Name -like $like) { return $w }
        }
        Start-Sleep -Milliseconds 200
    }
    throw "Window not found: $like"
}
function Inner($window, $controlType) {
    $condition = New-Object System.Windows.Automation.PropertyCondition ($ui::ControlTypeProperty), $controlType
    return $window.FindFirst($scopeDescendants, $condition)
}
function Focus($window, [int]$x, [int]$y) {
    [Probe]::MoveTo($x, $y); [Probe]::Spin(80); [Probe]::Left($true); [Probe]::Spin(40); [Probe]::Left($false)
    Start-Sleep -Milliseconds 400
}
function Wheel([int]$notches, [int]$gapMs = 0) { [ScrollCheck.Wheel]::Notches($notches, $gapMs) }

# Starts a scrolling capture of the area (x, y, w, h) through the shortcut (Ctrl+Alt+0: the release goes straight to scrolling).
function BeginCapture([int[]]$rect) {
    Chord @(0xA2, 0xA4, 0x30)
    WaitFor { [Probe]::Foreground() -eq 'Chippi capture' } 'selector foreground'
    Start-Sleep -Milliseconds 250
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Drag $rect[0] $rect[1] ($rect[0] + $rect[2] - 1) ($rect[1] + $rect[3] - 1)
    WaitFor { $null -ne (Find 'Done' -Button) } 'scrolling bar'
    $script:releaseToBar = $script:released.ElapsedMilliseconds
    WaitFor { (BarLine) -eq 'Scroll' } 'scrolling bar says Scroll'
    # The pointer over the area, where the wheel goes.
    [Probe]::MoveTo([int]($rect[0] + $rect[2] / 2), [int]($rect[1] + $rect[3] / 2)); Start-Sleep -Milliseconds 300
}
# Done, then Save: moves the saved PNG to scrolling-<name>.png and returns its path.
function FinishCapture([string]$name) {
    $before = @(Get-ChildItem "$results\saved" -Filter '*.png').Count
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Invoke 'Done'
    WaitFor { $null -ne (Find 'Save' -Button) } 'result bar' 30000
    $script:doneToResult = $script:invoked.ElapsedMilliseconds
    Shot "$name-result"
    Invoke 'Save'
    WaitFor { @(Get-ChildItem "$results\saved" -Filter '*.png').Count -gt $before } 'saved PNG' 30000
    Start-Sleep -Milliseconds 800
    $file = Get-ChildItem "$results\saved" -Filter '*.png' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $target = "$results\scrolling-$name.png"
    Move-Item -LiteralPath $file.FullName -Destination $target -Force
    WaitFor { $null -eq (Find 'Save' -Button) } 'result bar closes'
    return $target
}
function Idle {
    if ($null -ne (Find 'Cancel' -Button)) { Invoke 'Cancel'; WaitFor { $null -eq (Find 'Cancel' -Button) } 'session closes' }
}
function Verify([string]$name, [string]$path, [int]$minRows, [int]$lastMin, [switch]$Bars) {
    $header = $null; $footer = $null; $page = $null
    if ($Bars) { $header = $headerRgb; $footer = $footerRgb; $page = $pageRgb }
    $v = [ScrollVerify]::Check($path, $header, $footer, $page)
    Note "VERIFY $name $($v.Summary)"
    $found.verified[$name] = [ordered]@{ summary = $v.Summary; first = $v.First; last = $v.Last; runs = $v.Runs; width = $v.Width; height = $v.Height; problems = @($v.Problems) }
    Require ($v.Ok) "$name has every row once, in order (see VERIFY in probe.log)"
    Require ($v.First -eq 0) "$name starts at row 0, the top of the first frame"
    Require ($v.Runs -ge $minRows) "$name holds at least $minRows rows (has $($v.Runs))"
    Require ($v.Last -ge $lastMin) "$name reaches row $lastMin (reaches $($v.Last))"
}

# First-boot region setup can show a console and take the keyboard about 30 seconds after logon.
Note 'Waiting for Sandbox startup tasks'
Start-Sleep -Seconds 45
$updater = Get-Process -Name rgnupdt -ErrorAction SilentlyContinue
if ($updater) { $updater | Wait-Process -Timeout 60 }
$edge = $null; $longText = $null; $chippi = $null
try {
    $screen = [System.Windows.Forms.SystemInformation]::PrimaryMonitorSize
    $W = $screen.Width; $H = $screen.Height
    $found.screen = "$W x $H"
    Note "Screen $W x $H"

    # The page: a fixed header, footer and sidebar, and 2,000 rows 40 high, each with its stripe (4 cells of 14 px, 36 high).
    $page = New-Object System.Text.StringBuilder
    [void]$page.Append(@'
<!doctype html><html><head><meta charset="utf-8"><title>Scroll page</title><style>
html,body{margin:0;background:#fff;font:14px "Segoe UI",sans-serif;color:#222}
#h{position:fixed;top:0;left:0;right:0;height:64px;background:#1F3A5F;color:#fff;line-height:64px;padding-left:220px;z-index:3;font-size:20px}
#f{position:fixed;bottom:0;left:0;right:0;height:48px;background:#3A1F5F;color:#fff;line-height:48px;padding-left:220px;z-index:3}
#s{position:fixed;top:64px;bottom:48px;left:0;width:200px;background:#E8EEF7;z-index:2;padding:16px;box-sizing:border-box;line-height:28px}
#c{margin:64px 0 48px 200px}
.r{height:40px;display:flex;align-items:flex-start;padding-left:24px}
.i{display:flex;height:36px;flex:none}.i b{width:14px;height:36px;display:block}
.t{height:36px;line-height:36px;padding-left:16px;white-space:nowrap;overflow:hidden}
</style></head><body><div id="h">Header</div><div id="s">Sidebar<br>Inbox<br>Sent<br>Drafts<br>Archive<br>Trash</div><div id="f">Footer</div><div id="c">
'@)
    $words = 'alpha', 'bravo', 'charlie', 'delta', 'echo', 'foxtrot', 'golf', 'hotel', 'india', 'juliet', 'kilo', 'lima'
    for ($n = 0; $n -lt 2000; $n++) {
        $d = @((($n -shr 9) -band 7), (($n -shr 6) -band 7), (($n -shr 3) -band 7), ($n -band 7))
        $cells = ($d | ForEach-Object { '<b style="background:' + [ScrollVerify]::Hex[$_] + '"></b>' }) -join ''
        $text = 'Row ' + $n + ' ' + $words[$n % 12] + ' ' + $words[($n * 7 + 3) % 12] + ' ' + $words[($n * 5 + 1) % 12]
        [void]$page.Append('<div class="r"><div class="i">' + $cells + '</div><div class="t">' + $text + '</div></div>')
    }
    [void]$page.Append('</div></body></html>')
    $pagePath = Join-Path $env:TEMP 'scroll-page.html'
    Set-Content -LiteralPath $pagePath -Value $page.ToString() -Encoding UTF8
    New-Item -ItemType Directory -Path "$results\saved" -Force | Out-Null

    $profile = Join-Path $env:APPDATA 'Chippi'
    New-Item -ItemType Directory -Path $profile -Force | Out-Null
    $settings = [ordered]@{ version = 1; app = @{ islandEnabled = $false }; modules = @{
        'screen-capture' = @{ enter = 2; saveFolder = "$results\saved"; shortcuts = @{ 'Capture scrolling area' = @{ modifiers = 3; key = 48 } } }
    }; features = @{ preset = 'custom'; installed = @('screen-capture'); capabilitiesUsed = @() } }
    $settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$profile\settings.json" -Encoding UTF8
    $env:CHIPPI_CAPTURE_TIMINGS = "$results\scrolling-capture-timings.txt"
    $chippi = Start-Process -FilePath 'C:\chippi\Chippi.exe' -PassThru
    Start-Sleep -Seconds 12
    Require (-not $chippi.HasExited) 'module starts'

    # --- Edge: by hand, the content alone, then the whole page ---
    $edgePath = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    Require ($null -ne $edgePath) 'Edge is installed'
    $edge = Start-Process -FilePath $edgePath -PassThru -ArgumentList @(
        "--user-data-dir=$env:TEMP\edge-scroll", '--no-first-run', '--no-default-browser-check', '--disable-extensions', '--force-renderer-accessibility',
        '--window-position=0,0', "--window-size=$W,$($H - 180)", '--new-window', ('file:///' + ($pagePath -replace '\\', '/')))
    $edgeWindow = TopWindow '*Scroll page*'
    Start-Sleep -Seconds 3
    $doc = Inner $edgeWindow ([System.Windows.Automation.ControlType]::Document)
    $r = if ($null -ne $doc) { $doc.Current.BoundingRectangle } else { New-Object System.Windows.Rect 0, 90, $W, ($H - 270) }
    if ($null -eq $doc) { Note 'Edge page element not found; assuming the page starts 90 px below the window top' }
    $docX = [int]$r.Left; $docY = [int]$r.Top; $docW = [int]$r.Width; $docH = [int]$r.Height
    Note "Edge page $docX,$docY ${docW}x$docH"
    Shot 'edge-page'

    $found.edgeAuto = @{}
    function EdgeHandRun([string]$name, [int[]]$rect) {
        Focus $edgeWindow ($docX + $docW / 2) ($docY + $docH / 2)
        Tap 0x24; Start-Sleep -Milliseconds 800   # Home: the page's top
        BeginCapture $rect
        $found.timings["$name-releaseToBar"] = $script:releaseToBar
        Shot "$name-start"
        $offered = AutoEnabled
        $found.edgeAuto["$name-offered"] = $offered
        Note "$name Auto offered on Edge: $offered"
        # Edge honors a posted wheel (Sandbox, 2026-11-20): Auto is offered, scrolls the page, and runs to End reached or is stopped.
        Require $offered "$name offers Auto on Edge"
        Invoke 'Auto'
        # The Auto button is disabled while it runs, so the wait is on the bar's line (15 s at most), then Stop.
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($clock.ElapsedMilliseconds -lt 15000 -and (BarLine) -notin @('End reached', 'Limit reached')) { Start-Sleep -Milliseconds 200 }
        Shot "$name-edge-auto"
        $line = BarLine
        $found.edgeAuto["$name-line"] = $line
        $found.edgeAuto["$name-autoMs"] = $clock.ElapsedMilliseconds
        Note "$name Auto on Edge after $($clock.ElapsedMilliseconds) ms: line '$line'"
        if ($line -eq 'Scrolling') { Invoke 'Stop'; Start-Sleep -Milliseconds 500 }
        # The page moved: the bar no longer says Scroll (nothing moved yet), and Auto was not turned off by a first step that did nothing.
        Require ((BarLine) -ne 'Scroll') "$name Auto scrolled the page on Edge"
        $found.edgeAuto["$name-stillOffered"] = (AutoEnabled)
        Invoke 'Cancel'; Start-Sleep -Milliseconds 800
        Focus $edgeWindow ($docX + $docW / 2) ($docY + $docH / 2)
        Tap 0x24; Start-Sleep -Milliseconds 800
        BeginCapture $rect
        # Slow, medium, fast; a step back up and down again.
        1..10 | ForEach-Object { Wheel -1; Start-Sleep -Milliseconds 220 }
        Wheel 2; Start-Sleep -Milliseconds 400; Wheel -2; Start-Sleep -Milliseconds 400
        1..25 | ForEach-Object { Wheel -2 30; Start-Sleep -Milliseconds 300 }
        1..25 | ForEach-Object { Wheel -3 8; Start-Sleep -Milliseconds 130 }
        # One far too fast: 4,000 px in 120 ms.
        Wheel -40 3
        $saw = $false; $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($clock.ElapsedMilliseconds -lt 4000 -and -not $saw) { $saw = ((BarLine) -eq 'Too fast'); if (-not $saw) { Start-Sleep -Milliseconds 100 } }
        Note "$name saw Too fast: $saw"
        $found.recorded["$name-sawTooFast"] = $saw
        Require $saw "$name says Too fast after a burst of 40 notches"
        Shot "$name-toofast"
        # Back up until the area overlaps the last stitched rows again, then on slowly.
        Start-Sleep -Milliseconds 1200
        $caught = $false
        foreach ($i in 1..60) {
            Wheel 3 20; Start-Sleep -Milliseconds 350
            if ((BarLine) -ne 'Too fast') { $caught = $true; break }
        }
        Require $caught "$name catches up after scrolling back"
        1..15 | ForEach-Object { Wheel -2 30; Start-Sleep -Milliseconds 300 }
        Shot "$name-caught-up"
        $path = FinishCapture $name
        $found.timings["$name-doneToResult"] = $script:doneToResult
        Verify $name $path 200 200 -Bars
    }
    EdgeHandRun 'edge-content' @(($docX + 210), $docY, [Math]::Min(640, $docW - 210 - 200), $docH)   # the content column starts at 224, its stripe with it
    Idle
    EdgeHandRun 'edge-whole' @($docX, $docY, $docW, $docH)
    Idle
    Stop-Process -Id $edge.Id -Force -ErrorAction SilentlyContinue
    Get-Process -Name msedge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    $edge = $null

    # --- Long text: by hand, then Auto to the end ---
    $longText = Start-Process -FilePath powershell.exe -PassThru -ArgumentList "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\scrolling-fixture.ps1 -Left 0 -Top 0 -Width 900 -Height $($H - 180)"
    $textWindow = TopWindow 'Long text' 30000
    Start-Sleep -Seconds 3
    $tb = $textWindow.Current.BoundingRectangle
    $textRect = @(([int]$tb.Left + 10), ([int]$tb.Top + 40), 700, ([int]$tb.Height - 50))
    Note "Long text window $($tb.Left),$($tb.Top) $($tb.Width)x$($tb.Height)"
    function TextTop { Focus $textWindow ($textRect[0] + 300) ($textRect[1] + 200); Chord @(0xA2, 0x24); Start-Sleep -Milliseconds 800 }

    TextTop
    BeginCapture $textRect
    Shot 'text-hand-start'
    1..12 | ForEach-Object { Wheel -1; Start-Sleep -Milliseconds 250 }
    1..20 | ForEach-Object { Wheel -3 25; Start-Sleep -Milliseconds 280 }
    $path = FinishCapture 'text-hand'
    Verify 'text-hand' $path 60 60

    TextTop
    BeginCapture $textRect
    Require (AutoEnabled) 'Long text offers Auto'
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Invoke 'Auto'
    Shot 'text-auto-running'
    WaitFor { (BarLine) -in @('End reached', 'Limit reached') } 'Auto ends' 240000
    $found.timings['text-auto-totalMs'] = $clock.ElapsedMilliseconds
    Require ((BarLine) -eq 'End reached') 'Auto of Long text says End reached'
    Shot 'text-auto-end'
    $path = FinishCapture 'text-auto'
    Verify 'text-auto' $path 980 998
    Stop-Process -Id $longText.Id -Force -ErrorAction SilentlyContinue
    $longText = $null

    # --- Notepad and Settings: Auto where it is offered; no stripe, so no row proof ---
    function AutoOnly([string]$name, [scriptblock]$open, [string]$like, [scriptblock]$area, [string]$processName) {
        $entry = [ordered]@{ rowProof = $false }
        $found.recorded[$name] = $entry
        try {
            & $open
            $window = TopWindow $like 20000
            Start-Sleep -Seconds 3
            $b = $window.Current.BoundingRectangle
            $rect = & $area $window $b
            Focus $window ($rect[0] + $rect[2] / 2) ($rect[1] + $rect[3] / 2)
            BeginCapture $rect
            $entry.autoOffered = (AutoEnabled)
            Shot "$name-start"
            if ($entry.autoOffered) {
                $clock = [Diagnostics.Stopwatch]::StartNew()
                Invoke 'Auto'
                WaitFor { (BarLine) -in @('End reached', 'Limit reached', 'Too fast') -or -not (AutoEnabled) } "$name Auto ends" 240000
                $entry.autoMs = $clock.ElapsedMilliseconds
                $entry.line = BarLine
            }
            $path = FinishCapture $name
            $image = [System.Drawing.Image]::FromFile($path); $entry.size = "$($image.Width) x $($image.Height)"; $image.Dispose()
        }
        catch { $entry.error = $_.Exception.Message; Note "$name (recorded, not required): $($_.Exception.Message)"; try { Idle } catch { } }
        finally { Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
    }
    $lines = 1..1000 | ForEach-Object { 'Line ' + $_.ToString('0000') + ' ' + ('lorem ipsum ' * (1 + $_ % 4)) }
    $notepadFile = Join-Path $env:TEMP 'notepad-lines.txt'
    Set-Content -LiteralPath $notepadFile -Value $lines -Encoding ASCII
    AutoOnly 'notepad' { Start-Process -FilePath notepad.exe -ArgumentList "`"$notepadFile`"" } '*notepad-lines*' {
        param($window, $b) @(([int]$b.Left + 20), ([int]$b.Top + 110), [Math]::Min(700, [int]$b.Width - 60), [Math]::Min(500, [int]$b.Height - 200))
    } 'Notepad'
    AutoOnly 'settings' { Start-Process 'ms-settings:about' } 'Settings' {
        param($window, $b) @(([int]$b.Left + 320), ([int]$b.Top + 110), [Math]::Min(700, [int]$b.Width - 340), [Math]::Min(500, [int]$b.Height - 180))
    } 'SystemSettings'

    $found.saved = @(Get-ChildItem $results -Filter 'scrolling-*.png' | Where-Object { $_.Name -notmatch 'edge-page|-start|-toofast|-caught|-result|-running|-end|timeout' } | ForEach-Object { $_.Name })
    $found.passed = $true
    $found.chippiRunning = (-not $chippi.HasExited)
    Require (-not $chippi.HasExited) 'Chippi still runs'
    Set-Content -LiteralPath "$results\scrolling-timings.txt" -Value (($found.timings.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value)" }) -join "`n")
}
finally {
    if ($chippi) { Stop-Process -Id $chippi.Id -Force -ErrorAction SilentlyContinue }
    if ($edge) { Stop-Process -Id $edge.Id -Force -ErrorAction SilentlyContinue }
    if ($longText) { Stop-Process -Id $longText.Id -Force -ErrorAction SilentlyContinue }
    $logs = Join-Path $env:LOCALAPPDATA 'Chippi\logs'
    if (Test-Path $logs) { Copy-Item -LiteralPath $logs -Destination "$results\scrolling-logs" -Recurse -Force }
}
return $found
