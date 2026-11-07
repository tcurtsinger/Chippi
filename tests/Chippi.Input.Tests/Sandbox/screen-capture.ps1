# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# Runs only inside Windows Sandbox, with clipboard redirection and networking disabled by run.ps1.
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'Run this check only inside Windows Sandbox through run.ps1.' }
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes
Add-Type -Namespace CaptureCheck -Name Clipboard -MemberDefinition '[DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber(); [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);'
[CaptureCheck.Clipboard]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$results = 'C:\results'
$found = [ordered]@{}
function Note([string]$text) { Add-Content -LiteralPath "$results\probe.log" -Value $text }
function Require([bool]$value, [string]$name) { if (-not $value) { throw "Screen capture check failed: $name" }; Note "PASS $name" }
function Aim {
    $front = [Probe]::Foreground()
    if ($front -notmatch 'Capture fixture|Results fixture|Chippi|Save [Aa]s') { throw "Unexpected foreground before input: $front" }
}
function Tap([uint16]$vk) { Aim; [Probe]::Key($vk, $true); [Probe]::Spin(25); [Probe]::Key($vk, $false); [Probe]::Spin(25) }
function Chord([uint16[]]$keys) {
    Aim
    foreach ($key in $keys) { [Probe]::Key($key, $true); [Probe]::Spin(15) }
    [array]::Reverse($keys)
    foreach ($key in $keys) { [Probe]::Key($key, $false); [Probe]::Spin(15) }
}
function Starter([int]$digit) { Chord @(0xA2, 0xA4, [uint16](0x30 + $digit)) }
function Click([int]$x, [int]$y) { Aim; [Probe]::MoveTo($x, $y); [Probe]::Spin(60); [Probe]::Left($true); [Probe]::Spin(35); [Probe]::Left($false) }
function Drag([int]$x1, [int]$y1, [int]$x2, [int]$y2, [switch]$Ctrl) {
    Aim; [Probe]::MoveTo($x1, $y1); [Probe]::Spin(60)
    if ($Ctrl) { [Probe]::Key(0xA2, $true) }
    [Probe]::Left($true); [Probe]::Spin(35); [Probe]::Glide($x1, $y1, $x2, $y2, 160); [Probe]::Left($false)
    if ($Ctrl) { [Probe]::Key(0xA2, $false) }
}
function WaitFor([scriptblock]$condition, [string]$description, [int]$milliseconds = 8000) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.ElapsedMilliseconds -lt $milliseconds) { if (& $condition) { return }; Start-Sleep -Milliseconds 50 }
    Note ("TIMEOUT $description foreground=" + [Probe]::Foreground() + " exited=" + $chippi.HasExited)
    if (-not $chippi.HasExited) {
        $pc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $chippi.Id
        $els = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $pc)
        foreach ($el in $els) { Note ("UIA " + $el.Current.ControlType.ProgrammaticName + " " + $el.Current.Name) }
    }
    throw "Timed out: $description"
}
function Shot([string]$name) {
    $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($screen.X, $screen.Y, 0, 0, $bitmap.Size); $graphics.Dispose()
    $bitmap.Save("$results\screen-capture-$name.png"); $bitmap.Dispose()
}
function Find([string]$name, [switch]$Prefix, [switch]$Last, [switch]$Button) {
    $processCondition = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $chippi.Id
    $elements = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $processCondition)
    $lastMatch = $null
    foreach ($element in $elements) {
        if ($Button -and $element.Current.ControlType -ne [System.Windows.Automation.ControlType]::Button -and $element.Current.ControlType -ne [System.Windows.Automation.ControlType]::MenuItem) { continue }
        if (($Prefix -and $element.Current.Name.StartsWith($name)) -or (-not $Prefix -and $element.Current.Name -eq $name)) { if (-not $Last) { return $element }; $lastMatch = $element }
    }
    return $lastMatch
}
function Invoke([string]$name, [switch]$Prefix, [switch]$Last) {
    WaitFor { $null -ne (Find $name -Prefix:$Prefix -Last:$Last -Button) } "control $name"
    $element = Find $name -Prefix:$Prefix -Last:$Last -Button
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke(); Start-Sleep -Milliseconds 150
}
function Fixture {
    $condition = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), 'Capture fixture'
    $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
    if ($null -eq $window) { throw 'Capture fixture window is absent' }
    $bounds = $window.Current.BoundingRectangle
    [Probe]::MoveTo([int]($bounds.Left + $bounds.Width / 2), [int]($bounds.Top + $bounds.Height / 2))
    [Probe]::Left($true); [Probe]::Spin(40); [Probe]::Left($false)
    WaitFor { [Probe]::Foreground() -eq 'Capture fixture' } 'fixture foreground'
    Note 'PASS fixture foreground'
}
function Region {
    Fixture; Starter 1
    WaitFor { [Probe]::Foreground() -eq 'Chippi capture' } 'region foreground'
    Start-Sleep -Milliseconds 250
    Drag 300 250 700 500
    WaitFor { $null -ne (Find 'Copy') } 'finish bar'
}
function Copied([uint32]$before, [switch]$KeepPage) {
    WaitFor { [CaptureCheck.Clipboard]::GetClipboardSequenceNumber() -ne $before -and [System.Windows.Forms.Clipboard]::ContainsImage() } 'image copy'
    if (-not $KeepPage) { WaitFor { [Probe]::Foreground() -eq 'Capture fixture' } 'keyboard hand-back' }
    return [System.Windows.Forms.Clipboard]::GetImage()
}

function CheckSaveAs {
    Region; Chord @(0xA2, 0xA0, 0x53)
    WaitFor { [Probe]::Foreground() -match 'Save [Aa]s' } 'Save as dialog'; Tap 0x1B
    WaitFor { [Probe]::Foreground() -eq 'Chippi capture' } 'Save as Cancel returns keyboard'
    WaitFor { $null -ne (Find 'Copy' -Button) } 'Save as Cancel keeps draft' 3000
    Note 'PASS Save as Cancel keeps draft'
}

function CheckText {
    Region; Invoke 'Text'; Click 330 280
    WaitFor { [System.Windows.Automation.AutomationElement]::FocusedElement.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit } 'text keyboard focus'
    Tap 0x54; Tap 0x45; Tap 0x53; Tap 0x54
    Chord @(0xA2, 0x41); Tap 0x51; Shot 'text-edit'
    Tap 0x1B
    Require ([Probe]::Foreground() -eq 'Chippi capture') 'first Escape leaves text editing'
    Shot 'text-committed'; Tap 0x1B
    Require ([Probe]::Foreground() -eq 'Capture fixture') 'second Escape cancels draft'

}

# First-boot region setup can show a console and take the keyboard about 30 seconds after logon.
# Let the disposable guest settle before starting the fixture and measuring Chippi.
Note 'Waiting for Sandbox startup tasks'
Start-Sleep -Seconds 45
$updater = Get-Process -Name rgnupdt -ErrorAction SilentlyContinue
if ($updater) { $updater | Wait-Process -Timeout 60 }
$fixture = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\capture-fixture.ps1' -PassThru
try {
    Start-Sleep -Seconds 4
    $profile = Join-Path $env:APPDATA 'Chippi'
    New-Item -ItemType Directory -Path $profile -Force | Out-Null
    New-Item -ItemType Directory -Path "$results\saved" -Force | Out-Null
    $settings = [ordered]@{ version = 1; app = @{ islandEnabled = $true }; captureHost = @{ openCapture = @{ modifiers = 3; key = 56 } }; modules = @{
        'screen-capture' = @{ enter = 2; saveFolder = "$results\saved"; shortcuts = @{
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
    if ((Get-Content C:\results\checks.txt) -match 'dialog-only') { CheckSaveAs; $found.scope='dialog-only'; $found.passed=$true; return $found }
    if ((Get-Content C:\results\checks.txt) -match 'text-only') { CheckText; $found.scope='text-only'; $found.passed=$true; return $found }

    Region; Shot 'selected'; Set-Clipboard -Value 'untouched'; Tap 0x1B
    Require ((Get-Clipboard -Raw).Trim() -eq 'untouched') 'Escape preserves clipboard'
    Region
    Tap 0x42; Drag 320 270 380 330; Shot 'redacted'
    $before = [CaptureCheck.Clipboard]::GetClipboardSequenceNumber(); Chord @(0xA2, 0x43)
    $image = Copied $before
    Require ($image.Width -eq 401 -and $image.Height -eq 251) 'source resolution'
    Require ($image.GetPixel(40, 40).ToArgb() -eq [System.Drawing.Color]::Black.ToArgb()) 'redaction copied as opaque black'
    $image.Save("$results\screen-capture-redacted-output.png"); $image.Dispose()

    Region; Tap 0x52; Invoke 'Color and width'; Invoke 'Fill'; Drag 325 275 420 350
    $before = [CaptureCheck.Clipboard]::GetClipboardSequenceNumber(); Chord @(0xA2, 0x43)
    $image = Copied $before
    Require ($image.GetPixel(60, 50).R -eq 0xE5 -and $image.GetPixel(60, 50).G -eq 0x48) 'filled shape pixels'
    $image.Dispose()
    Region; Tap 0x52; Drag 325 275 420 350
    Chord @(0xA2, 0x5A); Chord @(0xA2, 0x59); Tap 0x56; Drag 325 275 345 295; Shot 'object-selected'
    Tap 0x2E; Tap 0x50; Drag 330 280 450 350; Tap 0x45; Click 390 315; Shot 'erased'; Tap 0x1B

    CheckText

    Region; Tap 0x43; Drag 320 270 520 420; Invoke 'Crop' -Last
    $before = [CaptureCheck.Clipboard]::GetClipboardSequenceNumber(); Chord @(0xA2, 0x43)
    $image = Copied $before; Require ($image.Width -eq 200 -and $image.Height -eq 150) 'crop source resolution'; $image.Dispose()

    CheckSaveAs
    Invoke 'Save'
    WaitFor { @(Get-ChildItem "$results\saved" -Filter '*.png').Count -eq 1 } 'saved PNG'
    $saved = Get-ChildItem "$results\saved" -Filter '*.png' | Select-Object -First 1
    $image = [System.Drawing.Image]::FromFile($saved.FullName); Require ($image.Width -eq 401 -and $image.Height -eq 251) 'saved dimensions'; $image.Dispose()

    # A partial success must retry the failed save without copying again.
    Move-Item -LiteralPath 'C:\results\saved' -Destination 'C:\results\saved-original'
    Set-Content -LiteralPath 'C:\results\saved' -Value 'blocked destination'
    Region; Tap 0x0D
    WaitFor { $null -ne (Find 'Retry') } 'partial output Retry'
    $copiedSequence = [CaptureCheck.Clipboard]::GetClipboardSequenceNumber()
    Shot 'partial-failure'
    Remove-Item -LiteralPath 'C:\results\saved'
    Move-Item -LiteralPath 'C:\results\saved-original' -Destination 'C:\results\saved'
    Invoke 'Retry'
    WaitFor { [Probe]::Foreground() -eq 'Capture fixture' } 'Retry completes'
    Require ([CaptureCheck.Clipboard]::GetClipboardSequenceNumber() -eq $copiedSequence) 'Retry does not repeat successful copy'
    Require (@(Get-ChildItem "$results\saved" -Filter '*.png').Count -eq 2) 'Retry saves once'
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
    WaitFor { @(Get-ChildItem "$results\saved" -Filter '*.png').Count -eq 3 } 'second saved file'
    $latestSaved = Get-ChildItem "$results\saved" -Filter '*.png' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Fixture; Starter 8
    WaitFor { $null -ne (Find 'Capture region') } 'shared Capture panel'
    Shot 'panel'
    Invoke '401' -Prefix
    WaitFor { $null -ne (Find 'Annotate') } 'Recent result page'
    Shot 'recent-result'
    $before = [CaptureCheck.Clipboard]::GetClipboardSequenceNumber()
    Invoke 'Copy'; $copied = Copied $before -KeepPage
    Require ($copied.Width -eq 401 -and $copied.Height -eq 251) 'Recent copy uses retained source pixels'
    $copied.Dispose()
    Invoke 'Annotate'
    WaitFor { [Probe]::Foreground() -eq 'Chippi capture' } 'Recent result Annotate'
    Tap 0x1B
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
    # Provider integration consumes this selection, and the QR page gets the decoded fixture without a second freeze.
    Stop-Process -Id $chippi.Id -Force
    Stop-Process -Id $fixture.Id -Force
    $fixture = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\results-fixture.ps1' -PassThru
    $currentSettings = Get-Content -LiteralPath "$profile\settings.json" -Raw | ConvertFrom-Json
    $currentSettings.features.installed = @('screen-capture', 'screen-text')
    $currentSettings | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath "$profile\settings.json" -Encoding UTF8
    $chippi = Start-Process -FilePath 'C:\chippi\Chippi.exe' -PassThru
    Start-Sleep -Seconds 12
    Click 1000 650; Starter 1
    WaitFor { [Probe]::Foreground() -eq 'Chippi capture' } 'provider capture'
    Start-Sleep -Milliseconds 250; Drag 360 290 710 640
    WaitFor { $null -ne (Find 'Scan QR code') } 'installed provider button'
    $freezeCount = @(Get-Content "$results\screen-capture-timings.txt" | Where-Object { $_ -like 'freeze *' }).Count
    Invoke 'Scan QR code'
    WaitFor { $null -ne (Find 'https://example.com/chippi-check') } 'QR result from composed selection'
    Shot 'qr-provider-result'
    Require (@(Get-Content "$results\screen-capture-timings.txt" | Where-Object { $_ -like 'freeze *' }).Count -eq $freezeCount) 'provider does not recapture'
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
