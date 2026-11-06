# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# Native arrangement placement probe. Called only by the isolated Sandbox harness.
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'This check runs only in Windows Sandbox' }
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,UIAutomationClient,UIAutomationTypes
$answer = [ordered]@{ checks = [ordered]@{}; timingsMs = @(); limits = @(
    'One display and DPI; physical monitor changes remain owner checks',
    'Physical mixed-DPI placement, monitor removal, packaged activation and app-specific fixed/minimum sizes remain owner checks') }
$targetSource = @"
using System;
using System.Drawing;
using System.Windows.Forms;
public static class ArrangementTarget {
    [STAThread] public static void Main(string[] args) {
        if(args.Length>0 && args[0]=="no-window") { System.Threading.Thread.Sleep(120000); return; }
        Application.EnableVisualStyles();
        var form=new Form {Text=args.Length>0?args[0]:"Arrangement target",Size=new Size(360,220),StartPosition=FormStartPosition.Manual,Location=new Point(100,100)};
        if(args.Length>1 && args[1]=="fullscreen") { form.FormBorderStyle=FormBorderStyle.None;form.Bounds=Screen.PrimaryScreen.Bounds; }
        form.Controls.Add(new TextBox {Multiline=true,Dock=DockStyle.Fill,Text="Disposable arrangement fixture. No files or clipboard."});
        Application.Run(form);
    }
}
"@
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -OutputAssembly C:\results\ArrangementA.exe -OutputType WindowsApplication -TypeDefinition $targetSource
$targetSource | Set-Content C:\results\fixture.cs -Encoding UTF8
'<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0"><assemblyIdentity version="1.0.0.0" name="ArrangementAdminFixture"/><trustInfo xmlns="urn:schemas-microsoft-com:asm.v3"><security><requestedPrivileges><requestedExecutionLevel level="requireAdministrator" uiAccess="false"/></requestedPrivileges></security></trustInfo></assembly>' | Set-Content C:\results\admin.manifest -Encoding UTF8
$csc=Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
& $csc /nologo /target:winexe /out:C:\results\RequiresAdmin.exe /win32manifest:C:\results\admin.manifest /reference:System.Windows.Forms.dll /reference:System.Drawing.dll C:\results\fixture.cs
if ($LASTEXITCODE -ne 0) { throw 'Admin fixture compilation failed' }
foreach ($name in @('AfterAdmin','ShortcutAdmin','NoWindow','AfterCancel','OwnedFullscreen','AfterFullscreen')) { Copy-Item C:\results\ArrangementA.exe "C:\results\$name.exe" }
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
    public static IntPtr Find(string title) {return FindWindow(null,title);}
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
$uia=[System.Windows.Automation.AutomationElement]
function Named([string]$name,$scope=$uia::RootElement) {
    $condition=New-Object System.Windows.Automation.PropertyCondition ($uia::NameProperty),$name
    $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
}
function WaitNamed([string]$name) {
    $until=(Get-Date).AddSeconds(12)
    do { $element=Named $name;if($element){return $element};Start-Sleep -Milliseconds 100 } while((Get-Date) -lt $until)
    throw "Missing control: $name"
}
function Press($element) {
    $pattern=$null
    if($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)){$pattern.Invoke()}
    elseif($element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pattern)){$pattern.Select()}
    elseif($element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern,[ref]$pattern)){$pattern.Toggle()}
    else{throw 'Control cannot be invoked'}
    Start-Sleep -Milliseconds 150
}
function TextContaining([string]$text) {
    @($uia::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object {$_.Current.Name -like "*$text*"})
}
function WaitText([string]$text,[int]$seconds=12) {
    $until=(Get-Date).AddSeconds($seconds)
    do {if((TextContaining $text).Count -gt 0){return};Start-Sleep -Milliseconds 150} while((Get-Date) -lt $until)
    Shot 'missing-status'
    throw "Missing status: $text"
}
function OpenRun {Observe;Chord 57;Start-Sleep -Milliseconds 400}
function Window([string]$title) {
    $until = (Get-Date).AddSeconds(10)
    do {
        $window = [ArrangementProbe]::Find($title)
        if ($window -ne [IntPtr]::Zero) { return $window }
        Start-Sleep -Milliseconds 50
    } while ((Get-Date) -lt $until)
    Shot 'fixture-failed'
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
                $window = [ArrangementProbe]::Find($row.titleHint)
                $pidOfWindow = @(Get-Process | Where-Object { $_.MainWindowHandle -eq $window })
                foreach ($process in $pidOfWindow) { Stop-Process -Id $process.Id }
            } else { $reused[$row.id] = [ArrangementProbe]::Find($row.titleHint) }
        }
    }
    function ExtraRow([string]$exe,[string]$arguments,[string]$title) {
        @{id=[Guid]::NewGuid().ToString('N');appIdentity=$exe;target=$exe;arguments=$arguments;workingFolder='C:\results';app=$title;
            className=$arrangements[0].rows[0].className;titleHint=$title;displayId='missing-fixture-display';displayName='Disconnected fixture';dpi=96;
            placement=@{left=$work.Left+50;top=$work.Top+50;width=300;height=170;workLeft=$work.Left;workTop=$work.Top;workWidth=$work.Width;workHeight=$work.Height;maximized=$false;minimized=$false}}
    }
    $startMenu=Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    New-Item -ItemType Directory -Force $startMenu | Out-Null
    $linkPath=Join-Path $startMenu 'Arrangement admin shortcut.lnk'
    $wsh=New-Object -ComObject WScript.Shell
    $link=$wsh.CreateShortcut($linkPath);$link.TargetPath='C:\results\ShortcutAdmin.exe';$link.Arguments='Shortcut-admin';$link.Save()
    $bytes=[IO.File]::ReadAllBytes($linkPath)
    [BitConverter]::GetBytes(([BitConverter]::ToUInt32($bytes,20) -bor 0x2000)).CopyTo($bytes,20)
    [IO.File]::WriteAllBytes($linkPath,$bytes)
    $shell=New-Object -ComObject Shell.Application
    $registered=$null;$until=(Get-Date).AddSeconds(15)
    do {
        foreach($item in $shell.Namespace('shell:AppsFolder').Items()) {
            if($item.ExtendedProperty('System.Link.TargetParsingPath') -eq 'C:\results\ShortcutAdmin.exe'){$registered=$item.ExtendedProperty('System.AppUserModel.ID');break}
        }
        if(!$registered){Start-Sleep -Milliseconds 250}
    } while(!$registered -and (Get-Date) -lt $until)
    if(!$registered){throw 'Admin shortcut not registered in guest Start menu'}
    $shortcutRow=ExtraRow 'C:\results\ShortcutAdmin.exe' '' 'Shortcut-admin'
    $shortcutRow.appIdentity=$registered;$shortcutRow.target=$registered;$shortcutRow.workingFolder=''
    $safety=@{id='safety';name='Safety';shortcut=@{modifiers='control, alt';key=77};rows=@(
        (ExtraRow 'C:\results\RequiresAdmin.exe' 'Requires-admin' 'Requires-admin'),$shortcutRow,(ExtraRow 'C:\results\AfterAdmin.exe' 'After-admin' 'After-admin'))}
    $cancel=@{id='cancel';name='Cancel fixture';rows=@((ExtraRow 'C:\results\NoWindow.exe' 'no-window' 'Waiting'),(ExtraRow 'C:\results\AfterCancel.exe' 'Must-not-open' 'Must-not-open'))}
    $timeout=@{id='timeout';name='Timeout fixture';rows=@((ExtraRow 'C:\results\NoWindow.exe' 'no-window' 'Timeout'))}
    $fullscreen=@{id='fullscreen';name='Fullscreen fixture';shortcut=@{modifiers='control, alt';key=78};rows=@(
        (ExtraRow 'C:\results\OwnedFullscreen.exe' 'Owned-fullscreen fullscreen' 'Owned-fullscreen'),(ExtraRow 'C:\results\AfterFullscreen.exe' 'After-fullscreen' 'After-fullscreen'))}
    $fixtureGroups=@($arrangements)
    $arrangements+=@($safety,$cancel,$timeout,$fullscreen)
    $profile = Join-Path $env:APPDATA 'Chippi-dev\sandbox'
    New-Item -ItemType Directory -Force $profile | Out-Null
    @{version=1;app=@{islandEnabled=$true};modules=@{'window-layouts'=@{arrangements=$arrangements};'quick-actions'=@{shown=@('snap')};shortcuts=@{bindings=@(@{id='open-actions';door='key';keys=@{modifiers='control, alt';key=57};action=@{kind='chippi';value='page/quick-actions';name='Actions'}},@{id='open-settings';door='key';keys=@{modifiers='control, alt';key=48};action=@{kind='chippi';value='page/settings';name='Settings'}})}};
        features=@{preset='custom';installed=@('window-layouts','quick-actions','shortcuts');capabilitiesUsed=@()}} |
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
                if (-not (Fits $row ([ArrangementProbe]::Find($row.titleHint)))) { $ready=$false }
            }
            if ($ready) { break }
            Start-Sleep -Milliseconds 50
        } while ($watch.ElapsedMilliseconds -lt 15000)
        $answer.timingsMs += @{arrangement=$arrangement.name;ready=$watch.ElapsedMilliseconds}
        Check "group-$group-placement-and-state" $ready
        foreach ($row in $arrangement.rows | Where-Object { -not $_.placement.minimized }) {
            Check "reused-$($row.id)" ($reused[$row.id] -eq [ArrangementProbe]::Find($row.titleHint))
        }
        # All windows now exist: a second invocation must not create duplicates or change the foreground.
        Observe
        Chord (74+$group)
        Start-Sleep -Seconds 2
        $front = [ArrangementProbe]::GetForegroundWindow()
        $fixtureHandles = @($arrangements | ForEach-Object { $_.rows } | ForEach-Object { [ArrangementProbe]::Find($_.titleHint) })
        Check "group-$group-does-not-activate-fixtures" ($fixtureHandles -notcontains $front)
    }
    Check 'twelve-windows-no-duplicates' (@(Get-Process -Name ArrangementA,ArrangementB,ArrangementC -ErrorAction SilentlyContinue).Count -eq 12)
    foreach ($arrangement in $fixtureGroups) {
        $last = [ArrangementProbe]::Find($arrangement.rows[3].titleHint)
        [void][ArrangementProbe]::ShowWindow($last,9)
        Check "$($arrangement.id)-minimized-maximized-restores-maximized" ([ArrangementProbe]::IsZoomed($last))
    }
    Observe;Chord 77
    $null=Window 'After-admin'
    Start-Sleep -Milliseconds 300
    Check 'admin-executable-not-started' (@(Get-Process -Name RequiresAdmin -ErrorAction SilentlyContinue).Count -eq 0)
    Check 'admin-shortcut-not-started' (@(Get-Process -Name ShortcutAdmin -ErrorAction SilentlyContinue).Count -eq 0)
    OpenRun;WaitText 'Needs admin'
    Check 'two-admin-rows-skipped-with-reason' ((TextContaining 'Needs admin').Count -ge 2)
    Shot 'admin-refusal'
    Observe;Chord 78
    $owned=Window 'Owned-fullscreen'
    $null=Window 'After-fullscreen'
    # Window state and continuation prove the native fullscreen skip; the run-page states have fixture coverage.
    Check 'own-fullscreen-does-not-stop-next-row' ([ArrangementProbe]::Find('After-fullscreen') -ne [IntPtr]::Zero)
    Shot 'fullscreen-skipped'
    [void][ArrangementProbe]::ShowWindow($owned,7)
    Observe
    $settingsWindow=$null;$until=(Get-Date).AddSeconds(8)
    do {
        Chord 48
        $step=(Get-Date).AddSeconds(1)
        do {$settingsWindow=Named 'Chippi Settings';if($settingsWindow){break};Start-Sleep -Milliseconds 100} while((Get-Date) -lt $step)
    } while(!$settingsWindow -and (Get-Date) -lt $until)
    $settingsWindow=WaitNamed 'Chippi Settings'
    $nav=$settingsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object {$_.Current.Name -like '*Id = module:window-layouts,*' -or $_.Current.Name -like '*Id = window-layouts,*'} | Select-Object -First 1
    if(!$nav){throw 'Snap navigation missing'}
    Press $nav
    Press (WaitNamed 'New arrangement')
    (WaitNamed 'Name').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Captured fixture')
    $save=WaitNamed 'Save';$until=(Get-Date).AddSeconds(15)
    while(!$save.Current.IsEnabled -and (Get-Date) -lt $until){Start-Sleep -Milliseconds 100}
    Check 'capture-review-save-enabled' $save.Current.IsEnabled
    Shot 'capture-review'
    Press $save
    $saved=Get-Content (Join-Path $profile 'settings.json') -Raw | ConvertFrom-Json
    $captured=@($saved.modules.'window-layouts'.arrangements | Where-Object {$_.name -eq 'Captured fixture'})
    Check 'capture-saved-real-window-draft' ($captured.Count -eq 1 -and $captured[0].rows.Count -ge 12)
    Press (WaitNamed 'Launch Cancel fixture')
    $until=(Get-Date).AddSeconds(8)
    while(@(Get-Process -Name NoWindow -ErrorAction SilentlyContinue).Count -eq 0 -and (Get-Date) -lt $until){Start-Sleep -Milliseconds 100}
    Press (WaitNamed 'Cancel Cancel fixture')
    Start-Sleep -Milliseconds 400
    Check 'cancel-keeps-opened-process' (@(Get-Process -Name NoWindow -ErrorAction SilentlyContinue).Count -eq 1)
    Check 'cancel-prevents-pending-launch' (@(Get-Process -Name AfterCancel -ErrorAction SilentlyContinue).Count -eq 0)
    Shot 'cancelled'
    Press (WaitNamed 'Launch Timeout fixture')
    $settingsWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    OpenRun;WaitText 'Timed out' 75
    Check 'fixed-deadline-reports-timeout' ((TextContaining 'Timed out').Count -gt 0)
    Shot 'timeout'
    Shot 'after'
    $answer.passed = $true
}
finally {
    $answer | ConvertTo-Json -Depth 8 | Set-Content C:\results\arrangements-partial.json -Encoding UTF8
    if ($chippi -and -not $chippi.HasExited) { Stop-Process -Id $chippi.Id -ErrorAction SilentlyContinue }
    # These executable names are generated by this Sandbox check only.
    Get-Process -Name ArrangementA,ArrangementB,ArrangementC,ArrangementObserver,AfterAdmin,ShortcutAdmin,RequiresAdmin,NoWindow,AfterCancel,OwnedFullscreen,AfterFullscreen -ErrorAction SilentlyContinue | Stop-Process
}
$answer
