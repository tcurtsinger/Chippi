# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# Native arrangement placement probe. Called only by the isolated Sandbox harness.
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'This check runs only in Windows Sandbox' }
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$answer = [ordered]@{ checks = [ordered]@{}; timingsMs = @(); limits = @(
    'One display and DPI; physical monitor changes remain owner checks',
    'Capture UI, Cancel, timeout, run summaries and registered-app refusal must be checked with the completed screens') }
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -OutputAssembly C:\results\ArrangementA.exe -OutputType WindowsApplication -TypeDefinition @"
using System;
using System.Drawing;
using System.Windows.Forms;
public static class ArrangementTarget {
    [STAThread] public static void Main(string[] args) {
        Application.EnableVisualStyles();
        var form=new Form {Text=args.Length>0?args[0]:"Arrangement target",Size=new Size(360,220),StartPosition=FormStartPosition.Manual,Location=new Point(100,100)};
        form.Controls.Add(new TextBox {Multiline=true,Dock=DockStyle.Fill,Text="Disposable arrangement fixture. No files or clipboard."});
        Application.Run(form);
    }
}
"@
Copy-Item C:\results\ArrangementA.exe C:\results\ArrangementB.exe
Copy-Item C:\results\ArrangementA.exe C:\results\ArrangementC.exe
Copy-Item C:\results\ArrangementA.exe C:\results\ArrangementObserver.exe
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class ArrangementProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; public int Width {get{return Right-Left;}} public int Height {get{return Bottom-Top;}} }
    [StructLayout(LayoutKind.Sequential)] public struct Point {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] public struct Placement {public int Length,Flags,Show; public Point Min,Max; public Rect Normal;}
    [StructLayout(LayoutKind.Sequential)] struct Monitor {public int Length;public Rect Bounds,Work;public uint Flags;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls,string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr w,out Rect rect);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr w,ref Placement p);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr w,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr w);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr w,int command);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr w);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr w);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr w,uint flags);
    [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")] static extern bool GetMonitorInfo(IntPtr m,ref Monitor info);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr w,StringBuilder name,int size);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    public static Placement Read(IntPtr w) {var p=new Placement{Length=Marshal.SizeOf(typeof(Placement))};if(!GetWindowPlacement(w,ref p))throw new Exception("No placement");return p;}
    public static Rect Work(IntPtr w) {var m=new Monitor{Length=Marshal.SizeOf(typeof(Monitor))};if(!GetMonitorInfo(MonitorFromWindow(w,2),ref m))throw new Exception("No monitor");return m.Work;}
    public static Rect Normal(IntPtr w) {var p=Read(w);var m=new Monitor{Length=Marshal.SizeOf(typeof(Monitor))};GetMonitorInfo(MonitorFromWindow(w,2),ref m);var r=p.Normal;r.Left+=m.Work.Left-m.Bounds.Left;r.Right+=m.Work.Left-m.Bounds.Left;r.Top+=m.Work.Top-m.Bounds.Top;r.Bottom+=m.Work.Top-m.Bounds.Top;return r;}
    public static string Class(IntPtr w) {var name=new StringBuilder(256);GetClassName(w,name,name.Capacity);return name.ToString();}
}
"@
function Check([string]$name,[bool]$ok) {
    $answer.checks[$name] = $ok
    Note "arrangements $name : $ok"
    if (-not $ok) { Shot 'failed'; throw "Arrangement check failed: $name" }
}
function Shot([string]$name) {
    $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap $screen.Width,$screen.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen($screen.Left,$screen.Top,0,0,$bitmap.Size); $bitmap.Save("C:\results\arrangements-$name.png") }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}
function Window([string]$title) {
    $until = (Get-Date).AddSeconds(10)
    do {
        $window = [ArrangementProbe]::FindWindow($null,$title)
        if ($window -ne [IntPtr]::Zero) { return $window }
        Start-Sleep -Milliseconds 50
    } while ((Get-Date) -lt $until)
    throw "Fixture window did not open: $title"
}
function Chord([uint16]$key) {
    [Probe]::Key(0x11,$true); [Probe]::Key(0x12,$true); [Probe]::Key($key,$true)
    [Probe]::Key($key,$false); [Probe]::Key(0x12,$false); [Probe]::Key(0x11,$false)
}
function Observe {
    [void][ArrangementProbe]::ShowWindow($observer,4)
    [void][ArrangementProbe]::SetWindowPos($observer,[IntPtr](-1),40,40,360,220,0x10)
    [void][ArrangementProbe]::SetForegroundWindow($observer)
    [Probe]::MoveTo(80,90); [Probe]::Left($true); [Probe]::Left($false)
    Check 'observer-in-front' ([ArrangementProbe]::GetForegroundWindow() -eq $observer)
    [void][ArrangementProbe]::SetWindowPos($observer,[IntPtr](-2),0,0,0,0,0x13)
}
function Fits($row,[IntPtr]$window) {
    if ($window -eq [IntPtr]::Zero) { return $false }
    $p = [ArrangementProbe]::Read($window)
    $normal = [ArrangementProbe]::Normal($window)
    $saved = $row.placement
    $bounds = [math]::Abs($normal.Left-$saved.left) -le 2 -and [math]::Abs($normal.Top-$saved.top) -le 2 -and
        [math]::Abs($normal.Width-$saved.width) -le 2 -and [math]::Abs($normal.Height-$saved.height) -le 2
    if ($saved.minimized) { return $bounds -and [ArrangementProbe]::IsIconic($window) -and ((($p.Flags -band 2) -ne 0) -eq $saved.maximized) }
    return $bounds -and -not [ArrangementProbe]::IsIconic($window) -and ([ArrangementProbe]::IsZoomed($window) -eq $saved.maximized)
}
$targets = @()
$chippi = $null
try {
    $observerProcess = Start-Process C:\results\ArrangementObserver.exe -ArgumentList 'Observer' -PassThru
    $targets += $observerProcess
    $observer = Window 'Observer'
    $work = [ArrangementProbe]::Work($observer)
    $arrangements = @()
    for ($group=0; $group -lt 3; $group++) {
        $letter = @('A','B','C')[$group]
        $rows = @()
        for ($index=0; $index -lt 4; $index++) {
            $title = "Fixture-$letter-$index"
            $exe = "C:\results\Arrangement$letter.exe"
            $process = Start-Process $exe -ArgumentList $title -PassThru
            $targets += $process
            $window = Window $title
            $rows += @{ id="$letter-$index"; appIdentity=$exe; target=$exe; arguments=$title; workingFolder='C:\results';
                app="Fixture $letter"; className=[ArrangementProbe]::Class($window); titleHint=$title; displayId='missing-fixture-display'; displayName='Disconnected fixture'; dpi=96;
                placement=@{ left=$work.Left+10+$group*([int]($work.Width/3)); top=$work.Top+10+$index*([int]($work.Height/4));
                    width=[math]::Min(300,[int]($work.Width/3)-20); height=[math]::Min(170,[int]($work.Height/4)-20);
                    workLeft=$work.Left;workTop=$work.Top;workWidth=$work.Width;workHeight=$work.Height;
                    maximized=($index -eq 1 -or $index -eq 3);minimized=($index -ge 2) } }
        }
        $arrangements += @{ id="fixture-$letter";name="Fixture $letter";rows=$rows;shortcut=@{modifiers='control, alt';key=74+$group} }
    }
    Shot 'before'
    # Missing rows relaunch; all other rows must keep their existing handles.
    $reused = @{}
    foreach ($arrangement in $arrangements) {
        foreach ($row in $arrangement.rows) {
            if ($row.placement.minimized) {
                $window = [ArrangementProbe]::FindWindow($null,$row.titleHint)
                $pidOfWindow = @(Get-Process | Where-Object { $_.MainWindowHandle -eq $window })
                foreach ($process in $pidOfWindow) { Stop-Process -Id $process.Id }
            } else { $reused[$row.id] = [ArrangementProbe]::FindWindow($null,$row.titleHint) }
        }
    }
    $profile = Join-Path $env:APPDATA 'Chippi-dev\sandbox'
    New-Item -ItemType Directory -Force $profile | Out-Null
    @{version=1;app=@{islandEnabled=$true};modules=@{'window-layouts'=@{arrangements=$arrangements}};
        features=@{preset='custom';installed=@('window-layouts');capabilitiesUsed=@()}} |
        ConvertTo-Json -Depth 15 | Set-Content (Join-Path $profile 'settings.json') -Encoding UTF8
    $chippi = Start-Process C:\chippi\Chippi.exe -ArgumentList '--dev-profile sandbox' -PassThru
    Start-Sleep -Seconds 4
    for ($group=0; $group -lt 3; $group++) {
        Observe
        $watch = [Diagnostics.Stopwatch]::StartNew()
        Chord (74+$group)
        $arrangement = $arrangements[$group]
        do {
            [void][ArrangementProbe]::DwmFlush()
            $ready = $true
            foreach ($row in $arrangement.rows) {
                if (-not (Fits $row ([ArrangementProbe]::FindWindow($null,$row.titleHint)))) { $ready=$false }
            }
            if ($ready) { break }
            Start-Sleep -Milliseconds 50
        } while ($watch.ElapsedMilliseconds -lt 15000)
        $answer.timingsMs += @{arrangement=$arrangement.name;ready=$watch.ElapsedMilliseconds}
        Check "group-$group-placement-and-state" $ready
        foreach ($row in $arrangement.rows | Where-Object { -not $_.placement.minimized }) {
            Check "reused-$($row.id)" ($reused[$row.id] -eq [ArrangementProbe]::FindWindow($null,$row.titleHint))
        }
        # All windows now exist: a second invocation must not create duplicates or change the foreground.
        Observe
        Chord (74+$group)
        Start-Sleep -Seconds 2
        $front = [ArrangementProbe]::GetForegroundWindow()
        $fixtureHandles = @($arrangements | ForEach-Object { $_.rows } | ForEach-Object { [ArrangementProbe]::FindWindow($null,$_.titleHint) })
        Check "group-$group-does-not-activate-fixtures" ($fixtureHandles -notcontains $front)
    }
    Check 'twelve-windows-no-duplicates' (@(Get-Process -Name ArrangementA,ArrangementB,ArrangementC -ErrorAction SilentlyContinue).Count -eq 12)
    foreach ($arrangement in $arrangements) {
        $last = [ArrangementProbe]::FindWindow($null,$arrangement.rows[3].titleHint)
        [void][ArrangementProbe]::ShowWindow($last,9)
        Check "$($arrangement.id)-minimized-maximized-restores-maximized" ([ArrangementProbe]::IsZoomed($last))
    }
    Shot 'after'
    $answer.passed = $true
}
finally {
    $answer | ConvertTo-Json -Depth 8 | Set-Content C:\results\arrangements-partial.json -Encoding UTF8
    if ($chippi -and -not $chippi.HasExited) { Stop-Process -Id $chippi.Id -ErrorAction SilentlyContinue }
    # These executable names are generated by this Sandbox check only.
    Get-Process -Name ArrangementA,ArrangementB,ArrangementC,ArrangementObserver -ErrorAction SilentlyContinue | Stop-Process
}
$answer
