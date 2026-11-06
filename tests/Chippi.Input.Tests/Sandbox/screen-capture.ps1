# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# Runs only inside Windows Sandbox, with clipboard redirection and networking disabled by run.ps1.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes
Add-Type -Namespace CaptureCheck -Name Clipboard -MemberDefinition '[DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();'
$results = 'C:\results'
$found = [ordered]@{}
function Note([string]$text) { Add-Content -LiteralPath "$results\probe.log" -Value $text }
function Require([bool]$value, [string]$name) { if (-not $value) { throw "Screen capture check failed: $name" }; Note "PASS $name" }
function Aim {
    $front = [Probe]::Foreground()
    if ($front -notmatch 'Capture fixture|Chippi|Save [Aa]s') { throw "Unexpected foreground before input: $front" }
}
function Tap([uint16]$vk) { Aim; [Probe]::Key($vk, $true); [Probe]::Spin(25); [Probe]::Key($vk, $false); [Probe]::Spin(25) }
function Chord([uint16[]]$keys) {
    Aim
    foreach ($key in $keys) { [Probe]::Key($key, $true); [Probe]::Spin(15) }
    [array]::Reverse($keys)
    foreach ($key in $keys) { [Probe]::Key($key, $false); [Probe]::Spin(15) }
}
function Starter([int]$digit) { Chord @(0xA2, 0xA4, [uint16](0x30 + $digit)) }
function Click([int]$x, [int]$y) { Aim; [Probe]::MoveTo($x, $y); [Probe]::Left($true); [Probe]::Spin(35); [Probe]::Left($false) }
function Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2, [switch]$Ctrl) {
    Aim; [Probe]::MoveTo($x1, $y1); [Probe]::Spin(60)
    if ($Ctrl) { [Probe]::Key(0xA2, $true) }
    [Probe]::Left($true); [Probe]::Spin(35); [Probe]::Glide($x1, $y1, $x2, $y2, 160); [Probe]::Left($false)
    if ($Ctrl) { [Probe]::Key(0xA2, $false) }
}
function WaitFor([scriptblock]$condition, [string]$name, [int]$milliseconds = 8000) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.ElapsedMilliseconds -lt $milliseconds) { if (& $condition) { return }; Start-Sleep -Milliseconds 50 }
    throw "Timed out: $name"
}
function Shot([string]$name) {
    $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($screen.X, $screen.Y, 0, 0, $bitmap.Size); $graphics.Dispose()
    $bitmap.Save("$results\screen-capture-$name.png"); $bitmap.Dispose()
}
function Find([string]$name, [switch]$Prefix, [switch]$Last) {
    $processCondition = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $chippi.Id
    $elements = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $processCondition)
    $lastMatch = $null
    foreach ($element in $elements) {
        if (($Prefix -and $element.Current.Name.StartsWith($name)) -or (-not $Prefix -and $element.Current.Name -eq $name)) { if (-not $Last) { return $element }; $lastMatch = $element }
    }
    return $lastMatch
}
function Invoke([string]$name, [switch]$Prefix, [switch]$Last) {
    WaitFor { $null -ne (Find $name -Prefix:$Prefix -Last:$Last) } "control $name"
    $element = Find $name -Prefix:$Prefix -Last:$Last
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke(); Start-Sleep -Milliseconds 150
}
function Fixture { [Probe]::MoveTo(650, 400); [Probe]::Left($true); [Probe]::Left($false); Start-Sleep -Milliseconds 150; Require ([Probe]::Foreground() -eq 'Capture fixture') 'fixture foreground' }
function Region {
    Fixture; Starter 1
    WaitFor { [Probe]::Foreground() -eq 'Chippi capture' } 'region foreground'
    Start-Sleep -Milliseconds 250
    Drag 300 250 700 500
    WaitFor { $null -ne (Find 'Copy') } 'finish bar'
}
function Copied([uint32]$before) {
    WaitFor { [CaptureCheck.Clipboard]::GetClipboardSequenceNumber() -ne $before -and [System.Windows.Forms.Clipboard]::ContainsImage() } 'image copy'
    WaitFor { [Probe]::Foreground() -eq 'Capture fixture' } 'keyboard hand-back'
    return [System.Windows.Forms.Clipboard]::GetImage()
}

$fixture = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\capture-fixture.ps1' -PassThru
try {
    Start-Sleep -Seconds 4
    $profile = Join-Path $env:APPDATA 'Chippi'
    New-Item -ItemType Directory -Path $profile -Force | Out-Null
    New-Item -ItemType Directory -Path "$results\saved" -Force | Out-Null
    $settings = [ordered]@{ version = 1; app = @{ islandEnabled = $false }; modules = @{
        'screen-capture' = @{ saveFolder = "$results\saved"; shortcuts = @{
            'Capture region' = @{ modifiers = 3; key = 49 }; 'Capture window' = @{ modifiers = 3; key = 50 };
            'Capture full screen' = @{ modifiers = 3; key = 51 }; 'Capture all displays' = @{ modifiers = 3; key = 52 };
            'Capture freeform' = @{ modifiers = 3; key = 53 }; 'Capture last region' = @{ modifiers = 3; key = 54 };
            'Open recent captures' = @{ modifiers = 3; key = 57 }
        } }
    }; features = @{ preset = 'custom'; installed = @('screen-capture'); capabilitiesUsed = @() } }
    $settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$profile\settings.json" -Encoding UTF8
    $env:CHIPPI_CAPTURE_TIMINGS = "$results\screen-capture-timings.txt"
    $chippi = Start-Process -FilePath 'C:\chippi\Chippi.exe' -PassThru
    Start-Sleep -Seconds 12
    Require (-not $chippi.HasExited) 'module starts'

    Region; Shot 'selected'; Set-Clipboard -Value 'untouched'; Tap 0x1B
    Require ((Get-Clipboard -Raw).Trim() -eq 'untouched') 'Escape preserves clipboard'
    Region
    Invoke 'Redact'; Drag 320 270 380 330; Shot 'redacted'
    $before = [CaptureCheck.Clipboard]::GetClipboardSequenceNumber(); Invoke 'Copy'
    $image = Copied $before
    Require ($image.Width -eq 400 -and $image.Height -eq 250) 'source resolution'
    Require ($image.GetPixel(40, 40).ToArgb() -eq [System.Drawing.Color]::Black.ToArgb()) 'redaction copied as opaque black'
    $image.Save("$results\screen-capture-redacted-output.png"); $image.Dispose()

    Region; Invoke 'Rectangle'; Drag 325 275 420 350
    Chord @(0xA2, 0x5A); Chord @(0xA2, 0x59); Invoke 'Select'; Drag 325 275 345 295; Shot 'object-selected'
    Tap 0x2E; Invoke 'Pen'; Drag 330 280 450 350; Invoke 'Eraser'; Click 390 315; Shot 'erased'; Tap 0x1B

    Region; Invoke 'Text'; Click 330 280
    Tap 0x54; Tap 0x45; Tap 0x53; Tap 0x54
    Chord @(0xA2, 0x41); Tap 0x51; Shot 'text-edit'
    Tap 0x1B
    Require ([Probe]::Foreground() -eq 'Chippi capture') 'first Escape leaves text editing'
    Shot 'text-committed'; Tap 0x1B
    Require ([Probe]::Foreground() -eq 'Capture fixture') 'second Escape cancels draft'

    Region; Invoke 'Crop'; Drag 320 270 520 420; Invoke 'Crop' -Last
    $before = [CaptureCheck.Clipboard]::GetClipboardSequenceNumber(); Chord @(0xA2, 0x43)
    $image = Copied $before; Require ($image.Width -eq 200 -and $image.Height -eq 150) 'crop source resolution'; $image.Dispose()

    Region; Chord @(0xA2, 0xA0, 0x53)
    WaitFor { [Probe]::Foreground() -match 'Save [Aa]s' } 'Save as dialog'; Tap 0x1B
    Require ([Probe]::Foreground() -eq 'Chippi capture') 'Save as Cancel keeps draft'
    Invoke 'Save'
    WaitFor { @(Get-ChildItem "$results\saved" -Filter '*.png').Count -eq 1 } 'saved PNG'
    $saved = Get-ChildItem "$results\saved" -Filter '*.png' | Select-Object -First 1
    $image = [System.Drawing.Image]::FromFile($saved.FullName); Require ($image.Width -eq 400 -and $image.Height -eq 250) 'saved dimensions'; $image.Dispose()

    # Real-size Recent history: actual successful captures, with retention never touching saved files.
    foreach ($i in 1..50) {
        Fixture; Starter 1; WaitFor { [Probe]::Foreground() -eq 'Chippi capture' } 'history freeze'
        Start-Sleep -Milliseconds 200
        $before = [CaptureCheck.Clipboard]::GetClipboardSequenceNumber()
        Drag 300 250 700 500 -Ctrl
        $image = Copied $before; $image.Dispose()
    }
    $cache = Join-Path $env:LOCALAPPDATA 'Chippi\data\screen-capture\recent'
    Require (@(Get-ChildItem $cache -Filter '*.png').Count -eq 50) 'retention keeps 50 owned images'
    Require (Test-Path -LiteralPath $saved.FullName) 'retention preserves external saved file'
    Region; Invoke 'Save'
    WaitFor { @(Get-ChildItem "$results\saved" -Filter '*.png').Count -eq 2 } 'second saved file'
    $latestSaved = Get-ChildItem "$results\saved" -Filter '*.png' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Fixture; Starter 9; WaitFor { $null -ne (Find 'Capture, ' -Prefix) } 'Recent in real Settings'; Shot 'settings-recent'
    Invoke 'Capture, ' -Prefix; Invoke 'Annotate'
    WaitFor { [Probe]::Foreground() -eq 'Chippi capture' } 'Annotate overlay'; Shot 'reopened'; Tap 0x1B
    # These use UI Automation, so they act on the named Settings page rather than a desktop coordinate.
    Starter 9; WaitFor { $null -ne (Find 'Capture, ' -Prefix) } 'Recent after Annotate'
    Shot 'settings-returned'
    Invoke 'Capture, ' -Prefix; Invoke 'Delete file'; Invoke 'Delete'
    WaitFor { -not (Test-Path -LiteralPath $latestSaved.FullName) } 'Recycle Bin deletion'
    Require (Test-Path -LiteralPath $saved.FullName) 'another saved file remains'
    Invoke 'Capture, ' -Prefix; Invoke 'Remove from recent'
    Require (@(Get-ChildItem $cache -Filter '*.png').Count -eq 49) 'Remove deletes only owned cache'
    Invoke 'Clear'; Invoke 'Clear' -Last
    WaitFor { $null -ne (Find 'No recent captures') } 'Clear Recent'
    Require (@(Get-ChildItem $cache -Filter '*.png').Count -eq 0) 'Clear removes owned cache'
    Require (Test-Path -LiteralPath $saved.FullName) 'Clear preserves saved file'
    Shot 'settings-cleared'
    $found.passed = $true
    $found.savedCount = @(Get-ChildItem "$results\saved" -Filter '*.png').Count
    $found.timings = @(Get-Content "$results\screen-capture-timings.txt")
}
finally {
    if ($chippi) { Stop-Process -Id $chippi.Id -Force -ErrorAction SilentlyContinue }
    if ($fixture) { Stop-Process -Id $fixture.Id -Force -ErrorAction SilentlyContinue }
    $logs = Join-Path $env:LOCALAPPDATA 'Chippi\logs'
    if (Test-Path $logs) { Copy-Item -LiteralPath $logs -Destination "$results\screen-capture-logs" -Recurse -Force }
}
return $found
