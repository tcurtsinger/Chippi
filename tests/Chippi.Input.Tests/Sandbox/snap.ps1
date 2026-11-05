# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# Called only inside the existing isolated Sandbox harness. No clipboard access.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SnapProbe {
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int X,Y,R,B; public int Width {get{return R-X;}} public int Height {get{return B-Y;}} }
    [StructLayout(LayoutKind.Sequential)] struct Monitor { public int Size; public Rect Bounds,Work; public uint Flags; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr w,out Rect r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr w,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr w);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr w,int cmd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr w);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr w,uint flags);
    [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")] static extern bool GetMonitorInfo(IntPtr m,ref Monitor i);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr w,int a,out Rect r,int n);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr p,uint access,out IntPtr t);
    [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr t,int c,out int v,int s,out int n);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    public static bool Elevated() {
        IntPtr t; if(!OpenProcessToken(System.Diagnostics.Process.GetCurrentProcess().Handle,8,out t))throw new Exception("No token");
        try { int v,n; if(!GetTokenInformation(t,20,out v,4,out n))throw new Exception("No elevation"); return v!=0; }
        finally { CloseHandle(t); }
    }
    public static Rect Visible(IntPtr w) { Rect r; if(DwmGetWindowAttribute(w,9,out r,16)!=0)GetWindowRect(w,out r); return r; }
    public static Rect Work(IntPtr w) { var m=new Monitor{Size=Marshal.SizeOf(typeof(Monitor))}; GetMonitorInfo(MonitorFromWindow(w,2),ref m);return m.Work; }
}
"@
# UI Automation and DWM use physical pixels. Keep SendInput normalization in the same coordinate space.
[void][SnapProbe]::SetThreadDpiAwarenessContext([IntPtr](-4))
$uia = [System.Windows.Automation.AutomationElement]
$answer = [ordered]@{ checks = [ordered]@{}; timingsMs = @(); limits = @('Sandbox has one display and one DPI; physical mixed-DPI and monitor disconnect remain live checks') }
function Check([string]$name, [bool]$ok) {
    $answer.checks[$name] = $ok
    Note "snap $name : $ok"
    if (-not $ok) { Shot 'failed'; throw "Snap check failed: $name" }
}
function Named([string]$name, $scope = $uia::RootElement) {
    $condition = New-Object System.Windows.Automation.PropertyCondition ($uia::NameProperty), $name
    $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function WaitNamed([string]$name) {
    $end = (Get-Date).AddSeconds(12)
    do { $element = Named $name; if ($element) { return $element }; Start-Sleep -Milliseconds 150 } while ((Get-Date) -lt $end)
    Shot "missing"
    throw "No UI element: $name"
}
function Press($element) {
    $pattern = $null
    foreach ($type in @([System.Windows.Automation.InvokePattern]::Pattern, [System.Windows.Automation.SelectionItemPattern]::Pattern, [System.Windows.Automation.TogglePattern]::Pattern)) {
        if ($element.TryGetCurrentPattern($type, [ref]$pattern)) { 
            if ($type -eq [System.Windows.Automation.InvokePattern]::Pattern) { $pattern.Invoke() }
            elseif ($type -eq [System.Windows.Automation.SelectionItemPattern]::Pattern) { $pattern.Select() }
            else { $pattern.Toggle() }
            Start-Sleep -Milliseconds 200
            return
        }
    }
    throw "Not pressable: $($element.Current.Name)"
}
function ClickNamed([string]$name) {
    $element = WaitNamed $name
    $r = $element.Current.BoundingRectangle
    if ($r.IsEmpty -or -not $element.Current.IsEnabled) { throw "Cannot click $name" }
    Note "click $name at $r"
    [Probe]::MoveTo([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    [Probe]::Left($true); [Probe]::Left($false)
    Start-Sleep -Milliseconds 250
}
function Shot([string]$name) {
    $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($screen.Left, $screen.Top, 0, 0, $bitmap.Size)
    $graphics.Dispose()
    $bitmap.Save("C:\results\snap-$name.png")
    $bitmap.Dispose()
}
function Chord([uint16]$key) {
    [Probe]::Key(0x11, $true); [Probe]::Key(0x12, $true); [Probe]::Key($key, $true)
    [Probe]::Key($key, $false); [Probe]::Key(0x12, $false); [Probe]::Key(0x11, $false)
}
function RectText($r) { "$($r.X),$($r.Y),$($r.Width),$($r.Height)" }
function Activate([IntPtr]$window) {
    [void][SnapProbe]::SetForegroundWindow($window)
    $r = [SnapProbe]::Visible($window)
    [Probe]::MoveTo($r.X + 80, $r.Y + 90)
    [Probe]::Left($true); [Probe]::Left($false)
    Start-Sleep -Milliseconds 200
}
function Place([uint16]$key, [IntPtr]$window) {
    Activate $window
    $before = RectText ([SnapProbe]::Visible($window))
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Chord $key
    do {
        [void][SnapProbe]::DwmFlush()
        $now = RectText ([SnapProbe]::Visible($window))
        if ($now -ne $before) { break }
        Start-Sleep -Milliseconds 10
    } while ($watch.ElapsedMilliseconds -lt 1500)
    $answer.timingsMs += [ordered]@{ key = [int]$key; firstChanged = $watch.ElapsedMilliseconds }
    Start-Sleep -Milliseconds 200
    [SnapProbe]::Visible($window)
}
$profileDir = Join-Path $env:APPDATA 'Chippi-dev\sandbox'
New-Item -ItemType Directory -Force -Path $profileDir | Out-Null
$bindings = @()
$verbs = @('SnapLeft','SnapRight','LeftThird','Maximize','Restore','OtherScreen','Center')
for ($i = 0; $i -lt $verbs.Count; $i++) {
    $bindings += @{ id = "snap-$i"; door = 'key'; keys = @{ modifiers = 'control, alt'; key = 49 + $i };
        action = @{ kind = 'window'; value = $verbs[$i]; name = $verbs[$i] } }
}
$bindings += @{ id = 'open-actions'; door = 'key'; keys = @{ modifiers = 'control, alt'; key = 57 };
    action = @{ kind = 'chippi'; value = 'page/quick-actions'; name = 'Actions' } }
$settings = @{ version = 1; app = @{ islandEnabled = $true }; modules = @{ shortcuts = @{ bindings = $bindings }; 'quick-actions' = @{ shown = @('snap') } };
    features = @{ preset = 'custom'; installed = @('window-layouts','shortcuts','quick-actions'); capabilitiesUsed = @() } }
$settings | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $profileDir 'settings.json') -Encoding UTF8
$chippi = Start-Process 'C:\chippi\Chippi.exe' -ArgumentList '--dev-profile sandbox' -PassThru
$targets = @()
try {
    Set-Content C:\results\Snap-A.txt 'Reading notes in the first window' -Encoding ASCII
    Set-Content C:\results\Snap-B.txt 'The second window must stay here' -Encoding ASCII
    if (Test-Path "$env:WINDIR\System32\notepad.exe") {
        $targets += Start-Process notepad.exe -ArgumentList 'C:\results\Snap-A.txt' -PassThru
        $targets += Start-Process notepad.exe -ArgumentList 'C:\results\Snap-B.txt' -PassThru
        $a = WaitNamed 'Snap-A.txt - Notepad'
        $b = WaitNamed 'Snap-B.txt - Notepad'
        $answer.target = 'Notepad'
    }
    else {
        Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing,System -TypeDefinition @"
using System;
using System.Threading;
using System.Windows.Forms;
public static class SnapEditors {
    public static void Start() {
        var ready = new ManualResetEvent(false);
        var thread = new Thread(() => {
            var a = Make("Snap-A", "Reading notes in the first window");
            var b = Make("Snap-B", "The second window must stay here");
            a.Show(); b.Show(); ready.Set();
            Application.Run();
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.WaitOne(10000)) throw new Exception("Editor UI thread did not start");
    }
    static Form Make(string name, string text) {
        var f = new Form { Text = name + ".txt - Probe editor", Width=650, Height=480, StartPosition=FormStartPosition.Manual, Left=120, Top=130 };
        f.Controls.Add(new TextBox { Multiline=true, Dock=DockStyle.Fill, Text=text });
        return f;
    }
}
"@
        [SnapEditors]::Start()
        $a = WaitNamed 'Snap-A.txt - Probe editor'
        $b = WaitNamed 'Snap-B.txt - Probe editor'
        $answer.target = 'Two WinForms editors'
        $answer.limits += 'Notepad is absent from this Sandbox image; standard WinForms editor windows substituted'
    }
    $wa = [IntPtr]$a.Current.NativeWindowHandle; $wb = [IntPtr]$b.Current.NativeWindowHandle
    [void][SnapProbe]::SetWindowPos($wa, [IntPtr]::Zero, 120, 130, 650, 480, 0x14)
    [void][SnapProbe]::SetWindowPos($wb, [IntPtr]::Zero, 450, 200, 640, 460, 0x14)
    Start-Sleep -Seconds 3
    $original = RectText ([SnapProbe]::Visible($wa)); $untouched = RectText ([SnapProbe]::Visible($wb))
    $answer.probeTokenElevation = [SnapProbe]::Elevated()
    $answer.probeAdminEnabled = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')
    $answer.probeIntegrity = (whoami /groups /fo csv | ConvertFrom-Csv | Where-Object { $_.'Group Name' -like 'Mandatory Label*' }).'Group Name'
    Shot 'before'
    $work = [SnapProbe]::Work($wa)
    $left = Place 49 $wa
    Check 'left-half' ([math]::Abs($left.X - $work.X) -le 1 -and [math]::Abs($left.Width - [math]::Round($work.Width / 2)) -le 1)
    $right = Place 50 $wa
    Check 'right-half' ([math]::Abs($right.R - $work.R) -le 1 -and [math]::Abs($right.Width - [math]::Round($work.Width / 2)) -le 1)
    $third = Place 51 $wa
    Check 'left-third' ([math]::Abs($third.Width - [math]::Round($work.Width / 3)) -le 1)
    $restored = Place 53 $wa
    Check 'repeated-snaps-restore-original' ((RectText $restored) -eq $original)
    Check 'other-window-untouched' ((RectText ([SnapProbe]::Visible($wb))) -eq $untouched)
    $null = Place 52 $wa
    Check 'maximize' ([SnapProbe]::IsZoomed($wa))
    $null = Place 53 $wa
    [void][SnapProbe]::ShowWindow($wa,3)
    Start-Sleep -Milliseconds 200
    $null = Place 49 $wa
    $null = Place 53 $wa
    Check 'restore-maximized-baseline' ([SnapProbe]::IsZoomed($wa))
    [void][SnapProbe]::ShowWindow($wa,9)
    [void][SnapProbe]::SetWindowPos($wa,[IntPtr]::Zero,160,170,600,450,0x14)
    $manual = RectText ([SnapProbe]::Visible($wa))
    $null = Place 50 $wa
    $null = Place 53 $wa
    Check 'manual-move-replaces-baseline' ((RectText ([SnapProbe]::Visible($wa))) -eq $manual)
    Shot 'placements'

    # Use Settings as the owner does, without changing any settings outside this dev profile.
    $null = Start-Process 'C:\chippi\Chippi.exe' -ArgumentList '--dev-profile sandbox' -PassThru
    Press (WaitNamed 'Settings')
    $settingsWindow = WaitNamed 'Chippi Settings'
    $nav = $settingsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.Name -like '*Id = module:window-layouts,*' -or $_.Current.Name -like '*Id = window-layouts,*' } | Select-Object -First 1
    if (-not $nav) { throw 'No Snap Settings navigation item' }
    Press $nav
    Shot 'settings-before'
    Press (WaitNamed 'Add layout')
    (WaitNamed 'Name').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Reading')
    Press (WaitNamed 'Side by side')
    Shot 'editor'
    Press (WaitNamed 'Save')
    $saved = Get-Content (Join-Path $profileDir 'settings.json') -Raw | ConvertFrom-Json
    $reading = $saved.modules.'window-layouts'.layouts | Where-Object { $_.name -eq 'Reading' }
    Check 'settings-created-reading-layout' ($null -ne $reading)
    # The first toggle on the page is Shift+drag. TogglePattern exercises Checked, not Click.
    $toggle = $settingsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $p=$null; $_.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern,[ref]$p) } | Select-Object -First 1
    Press $toggle
    Shot 'settings-after'
    $saved = Get-Content (Join-Path $profileDir 'settings.json') -Raw | ConvertFrom-Json
    Check 'uia-switch-enables-zones' ($saved.modules.'window-layouts'.shiftDragEnabled -eq $true)
    # Assign the newly created layout on the first connected display.
    $combo = $settingsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.Name -like 'Layout on *' } | Select-Object -First 1
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 150
    $choice = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.Name -eq 'Reading' } | Select-Object -First 1
    Press $choice
    $saved = Get-Content (Join-Path $profileDir 'settings.json') -Raw | ConvertFrom-Json
    Check 'display-assigned-reading' (@($saved.modules.'window-layouts'.displayLayouts.PSObject.Properties.Value) -contains $reading.id)

    # Closing Settings leaves the app running and the native drag observer installed.
    $settingsWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 300

    Activate $wa
    # Close an island left open behind Settings, then capture this editor on a fresh opening.
    [Probe]::Key(0x1B,$true); [Probe]::Key(0x1B,$false)
    Start-Sleep -Milliseconds 200
    Chord 57
    Start-Sleep -Milliseconds 900
    ClickNamed 'Snap'
    # Finish Actions' first-run selection before opening the selected Snap tile.
    if ((Named 'Done').Current.IsEnabled -and -not (Named 'Done').Current.IsOffscreen) { ClickNamed 'Done'; ClickNamed 'Snap' }
    Shot 'menu-before'
    $half = WaitNamed 'Halves: Left half'
    Check 'menu-captured-normal-target' $half.Current.IsEnabled
    Press $half
    Start-Sleep -Milliseconds 200
    $restore = WaitNamed 'Restore'
    Check 'menu-restore-becomes-available' $restore.Current.IsEnabled
    Shot 'menu'
    Press $restore
    Check 'menu-keeps-original-target' ((RectText ([SnapProbe]::Visible($wb))) -eq $untouched)
    [Probe]::Key(0x1B,$true); [Probe]::Key(0x1B,$false)
    Start-Sleep -Milliseconds 200

    # A real title-bar drag, driven one DWM present at a time. No Chippi marker is attached to SendInput.
    function Drag([string]$cancel) {
        [void][SnapProbe]::SetWindowPos($wa,[IntPtr]::Zero,160,170,600,450,0x14)
        Activate $wa
        $r = [SnapProbe]::Visible($wa)
        [Probe]::Key(0x10,$true)
        [Probe]::MoveTo($r.X + 160,$r.Y + 12); [Probe]::Left($true)
        for ($i=1; $i -le 12; $i++) {
            [void][SnapProbe]::DwmFlush()
            [Probe]::MoveTo($r.X + 160 + $i * 12,$r.Y + 12 + $i * 8)
        }
        Start-Sleep -Milliseconds 250
        $overlay = Named 'Snap zones'
        if ($cancel -eq '') {
            Check 'overlay-visible-during-shift-drag' ($null -ne $overlay)
            Shot 'overlay'
        }
        if ($cancel -eq 'escape') { [Probe]::Key(0x1B,$true); Start-Sleep -Milliseconds 40; [Probe]::Key(0x1B,$false) }
        if ($cancel -eq 'shift') { [Probe]::Key(0x10,$false); Start-Sleep -Milliseconds 80 }
        [Probe]::Left($false)
        Start-Sleep -Milliseconds 250
        [Probe]::Key(0x10,$false)
        [SnapProbe]::Visible($wa)
    }
    $dropped = Drag ''
    Check 'shift-drag-commits-zone' ([math]::Abs($dropped.Width - ($work.Width / 2 - 4)) -le 2)
    $cancelled = Drag 'escape'
    Check 'escape-cancels-placement' ($cancelled.Width -lt 610 -and $cancelled.Width -gt 570)
    $cancelled = Drag 'shift'
    Check 'releasing-shift-cancels-placement' ($cancelled.Width -lt 610 -and $cancelled.Width -gt 570)
    Check 'second-window-still-untouched' ((RectText ([SnapProbe]::Visible($wb))) -eq $untouched)
    Shot 'after'
    $answer.passed = $true
}
finally {
    $answer | ConvertTo-Json -Depth 8 | Set-Content C:\results\snap-partial.json -Encoding UTF8
    if ($chippi -and -not $chippi.HasExited) { Stop-Process -Id $chippi.Id -Force -ErrorAction SilentlyContinue }
    $logs = Join-Path $env:LOCALAPPDATA 'Chippi-dev\sandbox\logs'
    if (Test-Path $logs) { Copy-Item -LiteralPath $logs -Destination C:\results\chippi-logs -Recurse -Force }
}
$answer
