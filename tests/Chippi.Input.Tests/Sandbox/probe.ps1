# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# Runs inside Windows Sandbox, started by run.ps1. Drives the real hooks with SendInput, without Chippi's marker, so
# Chippi takes it for a hand's; aims it at target.ps1's window, and writes what happened to C:\results.

param([switch]$SnapMedium)

$ErrorActionPreference = 'Stop'
$results = 'C:\results'
$checks = (Get-Content -LiteralPath "$results\checks.txt") -split ','
if (($checks -contains 'snap' -or $checks -contains 'arrangements') -and -not $SnapMedium) {
    try { & (Join-Path $PSScriptRoot 'snap-medium.ps1'); shutdown.exe /s /t 5 }
    catch {
        @{ checks = $checks; error = $_.ToString(); errorLocation = $_.ScriptStackTrace } | ConvertTo-Json |
            Set-Content -LiteralPath "$results\results.json" -Encoding UTF8
        Set-Content -LiteralPath "$results\done.txt" -Value 'failed'
        shutdown.exe /s /t 5
    }
    return
}
$found = [ordered]@{ checks = $checks }

function Note([string]$text) { Add-Content -LiteralPath "$results\probe.log" -Value ('{0:HH:mm:ss.fff} {1}' -f (Get-Date), $text) }
function Text { if (Test-Path "$results\target-text.txt") { Get-Content -LiteralPath "$results\target-text.txt" -Raw } else { '' } }
function TargetLog { if (Test-Path "$results\target.log") { @(Get-Content -LiteralPath "$results\target.log") } else { @() } }

try {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class Probe
{
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int Dx, Dy; public uint Data, Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct KeyInput { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] struct Union { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public Union Data; }

    [DllImport("user32.dll")] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public static bool CapsOn() { return SendMessage(GetForegroundWindow(), 0x8042, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero; }
    static void Send(Input input) { SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input))); }

    static void Mouse(uint flags, int dx = 0, int dy = 0, uint data = 0)
    {
        var input = new Input { Type = 0 };
        input.Data.Mouse = new MouseInput { Dx = dx, Dy = dy, Flags = flags, Data = data };
        Send(input);
    }

    public static void MoveTo(int x, int y)
    {
        int width = GetSystemMetrics(0), height = GetSystemMetrics(1);
        Mouse(0x8001, (x * 65535 + width / 2) / (width - 1), (y * 65535 + height / 2) / (height - 1));
    }

    public static void Left(bool down) { Mouse(down ? 0x0002u : 0x0004u); }
    public static void Middle(bool down) { Mouse(down ? 0x0020u : 0x0040u); }
    public static void Right(bool down) { Mouse(down ? 0x0008u : 0x0010u); }
    public static void Back(bool down) { Mouse(down ? 0x0080u : 0x0100u, 0, 0, 1); }
    public static void Forward(bool down) { Mouse(down ? 0x0080u : 0x0100u, 0, 0, 2); }
    /// <summary>One tilt of the side wheel: positive is right, 120 a notch.</summary>
    public static void Tilt(int delta) { Mouse(0x01000, 0, 0, unchecked((uint)delta)); }

    public static void Key(ushort vk, bool down)
    {
        var input = new Input { Type = 1 };
        input.Data.Key = new KeyInput { Vk = vk, Flags = down ? 0u : 2u };
        Send(input);
    }

    /// <summary>A key as a keyboard sends it, with its scan code and the extended flag (Print Screen: 0x2C, 0x37).
    /// Windows doesn't match a bare-VK Print Screen to a registered hotkey (the capture check, 2026-11-06).</summary>
    public static void ExtendedKey(ushort vk, ushort scan, bool down)
    {
        var input = new Input { Type = 1 };
        input.Data.Key = new KeyInput { Vk = vk, Scan = scan, Flags = 1u | (down ? 0u : 2u) };
        Send(input);
    }

    /// <summary>From one point to another over about <paramref name="ms"/> milliseconds, a move a millisecond, as a hand would.</summary>
    public static void Glide(int x0, int y0, int x1, int y1, int ms)
    {
        timeBeginPeriod(1);
        for (int t = 1; t <= ms; t++) { MoveTo(x0 + (x1 - x0) * t / ms, y0 + (y1 - y0) * t / ms); Thread.Sleep(1); }
    }

    /// <summary>Waits <paramref name="ms"/> milliseconds on the high-resolution clock, without sleeping (a sleep ticks at 1 ms at best).</summary>
    public static void Spin(double ms)
    {
        long end = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(ms * System.Diagnostics.Stopwatch.Frequency / 1000);
        while (System.Diagnostics.Stopwatch.GetTimestamp() < end) { }
    }

    public static void Click(double gapMs)
    {
        Left(true); Left(false); Spin(gapMs); Left(true); Left(false);
    }

    public static void TapTwice(ushort vk, double gapMs)
    {
        Key(vk, true); Key(vk, false); Spin(gapMs); Key(vk, true); Key(vk, false);
    }

    /// <summary>Installs a mouse hook of its own and logs each event's own timestamp beside a Stopwatch reading taken in the hook.</summary>
    public static string[] HookClock(int events, double gapMs)
    {
        var rows = new System.Collections.Generic.List<string>();
        var ready = new ManualResetEvent(false);
        var done = new ManualResetEvent(false);
        var thread = new Thread(() =>
        {
            LowLevelProc proc = (code, wParam, lParam) =>
            {
                if (code >= 0 && (int)wParam == 0x0200)
                {
                    long now = System.Diagnostics.Stopwatch.GetTimestamp();
                    uint time = (uint)Marshal.ReadInt32(lParam, 8 + 4 + 4);
                    rows.Add(time + "," + (now * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                }
                return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            };
            IntPtr hook = SetWindowsHookEx(14, proc, IntPtr.Zero, 0);
            ready.Set();
            MSG msg;
            while (!done.WaitOne(0)) { if (PeekMessage(out msg, IntPtr.Zero, 0, 0, 1)) { DispatchMessage(ref msg); } else { Thread.Sleep(1); } }
            UnhookWindowsHookEx(hook);
        });
        thread.IsBackground = true;
        thread.Start();
        ready.WaitOne();
        for (int i = 0; i < events; i++) { MoveTo(300 + (i % 2) * 4, 300); Spin(gapMs); }
        Thread.Sleep(300);
        done.Set();
        thread.Join();
        return rows.ToArray();
    }

    /// <summary>Holds a RegisterHotKey of this process's own, as another app would: Chippi's attempt on the same keys is refused.</summary>
    static int hits;

    /// <summary>Presses the process's own held hotkey has heard.</summary>
    public static int Hits { get { return hits; } }

    public static bool HoldHotkey(uint modifiers, uint vk)
    {
        bool ok = false;
        var ready = new ManualResetEvent(false);
        var thread = new Thread(() =>
        {
            ok = RegisterHotKey(IntPtr.Zero, 77, modifiers, vk);
            ready.Set();
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { if (msg.Message == 0x312) System.Threading.Interlocked.Increment(ref hits); }
        });
        thread.IsBackground = true;
        thread.Start();
        ready.WaitOne();
        return ok;
    }

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);

    /// <summary>The title of the window in front: "Probe target" while the target holds the focus, something else once Start or a menu took it.</summary>
    public static string Foreground()
    {
        var text = new System.Text.StringBuilder(256);
        GetWindowText(GetForegroundWindow(), text, 256);
        return text.ToString();
    }

    /// <summary>Milliseconds until the window titled <paramref name="title"/> is in front, polled each millisecond; -1 if it never is.</summary>
    public static double WaitForeground(string title, int timeoutMs)
    {
        timeBeginPeriod(1);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (Foreground() == title) return Math.Round(clock.Elapsed.TotalMilliseconds, 1);
            Thread.Sleep(1);
        }
        return -1;
    }

    delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr Hwnd; public uint Message; public IntPtr WParam, LParam; public uint Time; public int X, Y; }
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LowLevelProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);

    /// <summary>Circles for <paramref name="ms"/> milliseconds at up to a thousand moves a second; returns the moves sent.</summary>
    public static int Circle(int x, int y, int radius, int ms)
    {
        timeBeginPeriod(1);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int moves = 0;
        while (clock.ElapsedMilliseconds < ms)
        {
            double angle = moves * 0.01;
            MoveTo(x + (int)(radius * Math.Cos(angle)), y + (int)(radius * Math.Sin(angle)));
            moves++;
            Thread.Sleep(1);
        }
        return moves;
    }
}
'@

    if ($checks -contains 'arrangements') {
        $found.arrangements = & (Join-Path $PSScriptRoot 'arrangements.ps1')
        return
    }

    if ($checks -contains 'snap') {
        $found.snap = & (Join-Path $PSScriptRoot 'snap.ps1')
        return
    }

    if ($checks -contains 'results') {
        $found.results = & (Join-Path $PSScriptRoot 'results.ps1')
        return
    }

    if ($checks -contains 'capture') {
        $found.capture = & (Join-Path $PSScriptRoot 'capture.ps1')
        return
    }

    # The app the input is aimed at, and its text box in front.
    Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\target.ps1'
    Start-Sleep -Seconds 5
    $x = 500; $y = 400
    [Probe]::MoveTo($x, $y); [Probe]::Left($true); [Probe]::Left($false)
    Start-Sleep -Milliseconds 500
    Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue

    # A dev profile with only the Radial menu: Middle and Back open it, and its four slices type which way was flicked.
    $profileDir = Join-Path $env:APPDATA 'Chippi-dev\sandbox'
    New-Item -ItemType Directory -Force -Path $profileDir | Out-Null
    # $app: more "app" settings (the pause's); $radial: more Radial menu settings. Each starts with a comma.
    function StartChippi([string]$app = '', [string]$radial = '', [string]$installed = '"radial-menu"', [string]$whole = '') {
        $settings = '{"version":1,"app":{"islandEnabled":false' + $app + '},' +
            '"modules":{"radial-menu":{"holdMiddle":true,"holdBack":true' + $radial + ',"slices":[' +
            '{"kind":"text","value":"up","name":"Up"},{"kind":"text","value":"right","name":"Right"},' +
            '{"kind":"text","value":"down","name":"Down"},{"kind":"text","value":"left","name":"Left"}]}},' +
            '"features":{"preset":"custom","installed":[' + $installed + '],"capabilitiesUsed":[]}}'
        if ($whole) { $settings = $whole }
        Set-Content -LiteralPath (Join-Path $profileDir 'settings.json') -Value $settings -Encoding ASCII
        $process = Start-Process -FilePath 'C:\chippi\Chippi.exe' -ArgumentList '--dev-profile sandbox' -PassThru
        Note "Chippi started, pid $($process.Id) (app$app; radial$radial)"
        Start-Sleep -Seconds 25
        [Probe]::MoveTo($x, $y); [Probe]::Left($true); [Probe]::Left($false)
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) { throw "Chippi exited ($($process.ExitCode))" }
        return $process
    }

    $debounceOnly = ($checks -contains 'debounce' -or $checks -contains 'timestamps') -and -not ($checks -contains 'radial' -or $checks -contains 'watchdog' -or $checks -contains 'cost')
    $debounceSettings = ''
    if ($debounceOnly) {
        # Debounce alone, both switches on (clicks 25 ms, keys 20 ms). CHIPPI_DEBOUNCE_INJECTED lets it act on SendInput.
        $debounceSettings = '{"version":1,"app":{"islandEnabled":false},' +
            '"modules":{"debounce":{"clickEnabled":true,"clickMs":25,"keyEnabled":true,"keyMs":20}},' +
            '"features":{"preset":"custom","installed":["debounce"],"capabilitiesUsed":[]}}'
        $env:CHIPPI_DEBOUNCE_INJECTED = '1'
    }
    if ($checks -contains 'scroll' -and -not ($checks -contains 'radial' -or $checks -contains 'watchdog' -or $checks -contains 'cost' -or $debounceOnly)) {
        # Smooth scrolling alone; the scroll check restarts Chippi with each of its own settings. CHIPPI_SMOOTH_INJECTED lets it act on SendInput.
        $debounceSettings = '{"version":1,"app":{"islandEnabled":false},"modules":{"smooth-scrolling":{"smoothEnabled":true}},' +
            '"features":{"preset":"custom","installed":["smooth-scrolling"],"capabilitiesUsed":[]}}'
        $env:CHIPPI_SMOOTH_INJECTED = '1'
    }
    $snippetsOnly = $checks -contains 'snippets' -and -not ($checks -contains 'radial' -or $checks -contains 'watchdog' -or $checks -contains 'cost' -or $debounceOnly)
    if ($snippetsOnly) {
        # Text snippets alone, expansion on. Escapes keep the file ASCII: an accent and an emoji (a surrogate pair) in one snippet.
        $long = '0123456789' * 20
        $debounceSettings = '{"version":1,"app":{"islandEnabled":false},"modules":{"text-snippets":{"expandWhileTyping":true,"snippets":[' +
            '{"id":"a","name":"Email","trigger":";em","text":"a@b.c","expand":"rightAway"},' +
            '{"id":"b","name":"Going","trigger":";go","text":"going","expand":"afterDelimiter"},' +
            '{"id":"c","name":"Lines","trigger":";ln","text":"caf\u00e9 \ud83d\ude00\nsecond","expand":"rightAway"},' +
            '{"id":"d","name":"Long","trigger":";lg","text":"' + $long + '","expand":"rightAway"},' +
            '{"id":"e","name":"Clip","trigger":";cp","text":"[{{clipboard}}]","expand":"rightAway"}]}},' +
            '"features":{"preset":"custom","installed":["text-snippets"],"capabilitiesUsed":[]}}'
    }
    $switcherOnly = $checks -contains 'switcher' -and -not ($checks -contains 'radial' -or $checks -contains 'watchdog' -or $checks -contains 'cost' -or $debounceOnly)
    if ($switcherOnly) {
        # The App switcher on Ctrl+Alt+J, and the Radial menu holding Middle with the switcher on its up slice.
        $debounceSettings = '{"version":1,"app":{"islandEnabled":false},"modules":{' +
            '"app-switcher":{"shortcut":{"modifiers":"control, alt","key":74}},' +
            '"radial-menu":{"holdMiddle":true,"slices":[{"kind":"chippi","value":"app-switcher/open","name":"Switcher"},' +
            '{"kind":"text","value":"right","name":"Right"},{"kind":"text","value":"down","name":"Down"},{"kind":"text","value":"left","name":"Left"}]}},' +
            '"features":{"preset":"custom","installed":["app-switcher","radial-menu"],"capabilitiesUsed":[]}}'
    }
    $shortcutsOnly = ($checks -contains 'shortcuts' -or $checks -contains 'caps' -or $checks -contains 'editor') -and -not ($checks -contains 'radial' -or $checks -contains 'watchdog' -or $checks -contains 'cost' -or $debounceOnly)
    function ShortcutSettings([string]$ignored = '') {
        $action = { param($name) '"action":{"kind":"window","value":"SnapLeft","name":"' + $name + '"}' }
        $bindings = @(
            ('{"id":"back","door":"mouse","button":"back",' + (& $action 'BackAct') + '}'),
            ('{"id":"fwd","door":"mouse","button":"forward",' + (& $action 'ForwardAct') + '}'),
            ('{"id":"mid","door":"mouse","button":"middle",' + (& $action 'MiddleAct') + '}'),
            ('{"id":"tr","door":"mouse","button":"wheelRight",' + (& $action 'TiltRightAct') + '}'),
            ('{"id":"tl","door":"mouse","button":"wheelLeft",' + (& $action 'TiltLeftAct') + '}'),
            ('{"id":"key","door":"key","keys":{"modifiers":"control, alt","key":75},' + (& $action 'KeyAct') + '}'),
            ('{"id":"winj","door":"key","keys":{"modifiers":"windows","key":74},' + (& $action 'WinJAct') + '}'),
            ('{"id":"winy","door":"key","keys":{"modifiers":"windows","key":89},' + (& $action 'WinYAct') + '}'))
        $caps = ''
        if ($checks -contains 'caps') {
            $caps = '"capsEnabled":true,"capsModifiers":"ctrl, alt, shift",'
            $bindings += '{"id":"caps-k","door":"caps","capsKey":75,"action":{"kind":"window","value":"Maximize","name":"CapsKAct"}}'
        }
        '{"version":1,"app":{"islandEnabled":false},"modules":{"shortcuts":{' + $caps + '"mouseEnabled":true,"mouseIgnoredApps":[' + $ignored + '],"bindings":[' + ($bindings -join ',') + ']}},' +
            '"features":{"preset":"custom","installed":["shortcuts"],"capabilitiesUsed":[]}}'
    }
    if ($shortcutsOnly) {
        # Shortcuts alone. CHIPPI_SHORTCUTS_PROBE: its fired bindings go to a file (the shared action runner isn't in the shell yet).
        # Another app holds Win+Y, so Chippi's RegisterHotKey for it is refused: the binding fires nothing and the other app hears it.
        $debounceSettings = ShortcutSettings
        if ($checks -contains 'editor') {
            # The page's editor: one saved binding (Ctrl+Alt+K), the Radial menu holding Middle, another app holding Win+Y.
            $debounceSettings = '{"version":1,"app":{"islandEnabled":false},"modules":{"radial-menu":{"holdMiddle":true,"slices":[]},' +
                '"shortcuts":{"mouseEnabled":true,"bindings":[{"id":"key","door":"key","keys":{"modifiers":"control, alt","key":75},"action":{"kind":"window","value":"SnapLeft","name":"KeyAct"}}]}},' +
                '"features":{"preset":"custom","installed":["shortcuts","radial-menu"],"capabilitiesUsed":[]}}'
        }
        $env:CHIPPI_SHORTCUTS_PROBE = "$results\actions.log"
        $holdWinY = [Probe]::HoldHotkey(8, 0x59)
        Note "another app holds Win+Y: $holdWinY"
    }
    $env:CHIPPI_INPUT_TIMINGS = "$results\hook-timings.csv"
    if ($checks -contains 'watchdog') { $env:CHIPPI_INPUT_DROP_HOOKS = '45' }
    if ($checks -contains 'pause') { $env:CHIPPI_INPUT_PROBE_FILTER = '1' }
    $chippi = if ($checks -contains 'pause') { StartChippi -radial ',"openOverFullscreen":false' } else { StartChippi -whole $debounceSettings }

    function Flick([string]$button, [int]$dx, [int]$dy) {
        $before = Text
        if ($button -eq 'middle') { [Probe]::Middle($true) } else { [Probe]::Back($true) }
        Start-Sleep -Milliseconds 400
        [Probe]::Glide($x, $y, $x + $dx, $y + $dy, 120)
        Start-Sleep -Milliseconds 150
        if ($button -eq 'middle') { [Probe]::Middle($false) } else { [Probe]::Back($false) }
        Start-Sleep -Milliseconds 1500
        [Probe]::MoveTo($x, $y)
        Start-Sleep -Milliseconds 300
        $typed = (Text).Substring($before.Length)
        Note "flick $button ($dx,$dy): typed '$typed'"
        return $typed
    }

    if ($checks -contains 'radial' -or $checks -contains 'watchdog') {
        $radial = [ordered]@{}
        # A quick click goes to the app, down and up.
        [Probe]::Middle($true); Start-Sleep -Milliseconds 60; [Probe]::Middle($false)
        Start-Sleep -Milliseconds 800
        $radial.middleClick = (TargetLog) -join '; '
        Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue

        # Held, flicked and let go: the slice it points at runs.
        $radial.middleHoldFlickUp = Flick 'middle' 0 -150
        $radial.backHoldFlickRight = Flick 'back' 150 0
        $radial.wheelInputReachedApp = (TargetLog) -join '; '
        Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue

        if ($checks -contains 'radial') {
            # Moved before the hold's 250 ms: the app's own drag, its down replayed before its up.
            [Probe]::Middle($true); [Probe]::Glide($x, $y, $x + 60, $y, 60); [Probe]::Middle($false)
            Start-Sleep -Milliseconds 800
            [Probe]::MoveTo($x, $y)
            $radial.middleDrag = (TargetLog) -join '; '
            Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue

            # Esc while it's open closes it, and neither Esc nor the button reaches the app.
            $before = Text
            [Probe]::Middle($true); Start-Sleep -Milliseconds 400
            [Probe]::Key(0x1B, $true); [Probe]::Key(0x1B, $false)
            Start-Sleep -Milliseconds 300
            [Probe]::Glide($x, $y, $x, $y - 150, 120); [Probe]::Middle($false)
            Start-Sleep -Milliseconds 1500
            [Probe]::MoveTo($x, $y)
            $radial.escDismiss = 'typed ''' + (Text).Substring($before.Length) + '''; app heard: ' + ((TargetLog) -join '; ')
            Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue
        }
        $found.radial = $radial
    }

    if ($checks -contains 'pause') {
        # A Debug build's probe filter pauses and keeps F24 (CHIPPI_INPUT_PROBE_FILTER). The "game" is a borderless window
        # covering the screen; target.ps1 logs to fullscreen.log there.
        function F24 { [Probe]::Key(0x87, $true); Start-Sleep -Milliseconds 80; [Probe]::Key(0x87, $false); Start-Sleep -Milliseconds 500 }
        function GameLog { if (Test-Path "$results\fullscreen.log") { @(Get-Content -LiteralPath "$results\fullscreen.log") } else { @() } }
        function StartGame {
            Remove-Item -LiteralPath "$results\fullscreen.log" -ErrorAction SilentlyContinue
            $game = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\target.ps1 -Fullscreen' -PassThru
            Start-Sleep -Seconds 4
            [Probe]::MoveTo($x, $y); [Probe]::Left($true); [Probe]::Left($false) # the game in front
            Start-Sleep -Milliseconds 800
            Remove-Item -LiteralPath "$results\fullscreen.log" -ErrorAction SilentlyContinue
            return $game
        }
        function EndGame($game) {
            Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 1
            [Probe]::MoveTo($x, $y); [Probe]::Left($true); [Probe]::Left($false) # the windowed target in front again
            Start-Sleep -Milliseconds 800
            Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue
        }
        $pause = [ordered]@{}
        Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue

        # Always on off (the default), Open over fullscreen apps off.
        F24
        $pause.windowedF24 = 'app heard: ' + ((TargetLog) -join '; ')
        $game = StartGame
        F24
        $pause.fullscreenF24 = 'game heard: ' + ((GameLog) -join '; ')
        Remove-Item -LiteralPath "$results\fullscreen.log" -ErrorAction SilentlyContinue
        [Probe]::Middle($true); Start-Sleep -Milliseconds 400; [Probe]::Glide($x, $y, $x, $y - 150, 120); [Probe]::Middle($false)
        Start-Sleep -Milliseconds 1500
        [Probe]::MoveTo($x, $y)
        $pause.radialOffOverGame = 'game heard: ' + ((GameLog) -join '; ')
        EndGame $game
        $pause.radialOffWindowed = Flick 'middle' 0 -150
        # F24 held while the game comes to the front: its up is still kept, so the game never sees an up without a down.
        Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue
        [Probe]::Key(0x87, $true)
        Start-Sleep -Milliseconds 300
        $game = StartGame
        [Probe]::Key(0x87, $false)
        Start-Sleep -Milliseconds 500
        $pause.heldAcrossThePause = 'game heard: ' + ((GameLog) -join '; ') + ' | app heard: ' + ((TargetLog) -join '; ')
        EndGame $game
        Stop-Process -Id $chippi.Id -Force
        Start-Sleep -Seconds 2

        # Always on.
        $chippi = StartChippi -app ',"inputAlwaysOn":true'
        $game = StartGame
        F24
        $pause.alwaysOnFullscreenF24 = 'game heard: ' + ((GameLog) -join '; ')
        EndGame $game
        Stop-Process -Id $chippi.Id -Force
        Start-Sleep -Seconds 2

        # Pause in these apps: the windowed target (powershell.exe) counts as fullscreen.
        $chippi = StartChippi -app ',"pauseApps":["C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe"]'
        Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue
        F24
        $pause.listedWindowedF24 = 'app heard: ' + ((TargetLog) -join '; ')
        Stop-Process -Id $chippi.Id -Force
        Start-Sleep -Seconds 2

        # No Radial menu: only the probe filter needs the keyboard, so its hook comes off for the game and back after it
        # (the hub's log says so).
        $chippi = StartChippi -installed ''
        $game = StartGame
        F24
        $pause.noRadialFullscreenF24 = 'game heard: ' + ((GameLog) -join '; ')
        EndGame $game
        F24
        $pause.noRadialWindowedF24 = 'app heard: ' + ((TargetLog) -join '; ')
        $found.pause = $pause
    }

    if ($checks -contains 'watchdog') {
        # Both hooks are dropped 45 s after start; the pointer and keys move for 10 s, then the wheel must work again.
        $wait = $chippi.StartTime.AddSeconds(47) - (Get-Date)
        if ($wait.TotalMilliseconds -gt 0) { Start-Sleep -Milliseconds ([int]$wait.TotalMilliseconds) }
        Note 'hooks dropped; moving the mouse and pressing Shift'
        for ($i = 0; $i -lt 10; $i++) {
            [Probe]::Circle($x, $y, 100, 900) | Out-Null
            [Probe]::Key(0x10, $true); [Probe]::Key(0x10, $false)
        }
        [Probe]::MoveTo($x, $y)
        Start-Sleep -Milliseconds 300
        $found.watchdog = [ordered]@{ afterDropFlickUp = (Flick 'middle' 0 -150) }
    }

    if ($checks -contains 'timestamps') {
        # The hook's own timestamp beside the Stopwatch, for 400 moves 2 ms apart: how often each ticks.
        $rows = [Probe]::HookClock(400, 2.0)
        Set-Content -LiteralPath "$results\hook-clock.csv" -Value (@('hookMs,stopwatchMs') + $rows) -Encoding ASCII
        $hook = $rows | ForEach-Object { [double]($_ -split ',')[0] }
        $watch = $rows | ForEach-Object { [double]($_ -split ',')[1] }
        $hookSteps = for ($i = 1; $i -lt $hook.Count; $i++) { $hook[$i] - $hook[$i - 1] }
        $watchSteps = for ($i = 1; $i -lt $watch.Count; $i++) { $watch[$i] - $watch[$i - 1] }
        $distinct = ($hookSteps | Where-Object { $_ -gt 0 } | Sort-Object -Unique | Select-Object -First 6) -join ' '
        $found.timestamps = [ordered]@{
            events = $rows.Count
            hookStepsZeroOrNot = "$(@($hookSteps | Where-Object { $_ -eq 0 }).Count) of $($hookSteps.Count) steps are 0 ms"
            hookSmallestNonZeroStepsMs = $distinct
            hookMaxStepMs = ($hookSteps | Measure-Object -Maximum).Maximum
            stopwatchMedianStepMs = [math]::Round((($watchSteps | Sort-Object)[[int]($watchSteps.Count / 2)]), 3)
        }
    }

    if ($checks -contains 'debounce') {
        $d = [ordered]@{}
        function Run([string]$name, [scriptblock]$act) {
            Remove-Item -LiteralPath "$results\target.log" -ErrorAction SilentlyContinue
            $before = (Text).Length
            & $act
            Start-Sleep -Milliseconds 700
            # The target's handlers are slow PowerShell: wait until its text file stops changing, or a read lands mid-burst
            # (and collides with the target's own append to target.log).
            $stable = 0; $last = -1
            for ($wait = 0; $wait -lt 120 -and $stable -lt 3; $wait++) {
                Start-Sleep -Milliseconds 500
                $now = (Get-Item -LiteralPath "$results\target-text.txt" -ErrorAction SilentlyContinue).Length
                if ($now -eq $last) { $stable++ } else { $stable = 0; $last = $now }
            }
            $typed = (Text).Substring($before)
            $d[$name] = [ordered]@{ target = ((TargetLog) -join '; '); typed = $typed }
            Note "debounce $name : $($d[$name].target) typed '$typed'"
            Start-Sleep -Milliseconds 300
        }
        [Probe]::MoveTo($x, $y)
        Run 'click2msGap' { [Probe]::Click(2) }
        Run 'click60msGap' { [Probe]::Click(60) }
        Run 'clickHeldThenSecond' { [Probe]::Left($true); [Probe]::Spin(300); [Probe]::Left($false); [Probe]::Spin(100); [Probe]::Left($true); [Probe]::Left($false) }
        Run 'key2msGap' { [Probe]::TapTwice(0x45, 2) }
        Run 'key60msGap' { [Probe]::TapTwice(0x45, 60) }
        Run 'otherKeysFastStillBoth' { [Probe]::Key(0x41, $true); [Probe]::Key(0x41, $false); [Probe]::Spin(2); [Probe]::Key(0x42, $true); [Probe]::Key(0x42, $false) }
        Run 'keyHeldRepeats' { [Probe]::Key(0x45, $true); [Probe]::Spin(300); [Probe]::Key(0x45, $true); [Probe]::Spin(33); [Probe]::Key(0x45, $true); [Probe]::Key(0x45, $false) }
        # A held key whose press bounced once (down, up 1 ms later, down 3 ms after that, dropped), then the keyboard's repeats and the up: the hold must still reach the app (#234).
        Run 'keyBounceThenHold' { [Probe]::Key(0x47, $true); [Probe]::Spin(1); [Probe]::Key(0x47, $false); [Probe]::Spin(3); [Probe]::Key(0x47, $true); [Probe]::Spin(500); for ($i = 0; $i -lt 10; $i++) { [Probe]::Key(0x47, $true); [Probe]::Spin(33) }; [Probe]::Key(0x47, $false) }
        Run 'fastTypingBurst' { for ($i = 0; $i -lt 20; $i++) { [Probe]::TapTwice(0x46, 40) } }
        [Probe]::Circle($x, $y, 150, 8000) | Out-Null
        # Enough key events for a p99 that isn't the first call's JIT: 4,000 (hook-timings.csv, keyboard row).
        Run 'keyBurst4000Events' { for ($i = 0; $i -lt 2000; $i++) { [Probe]::Key([uint16](0x41 + $i % 26), $true); [Probe]::Key([uint16](0x41 + $i % 26), $false); [Probe]::Spin(6) } }
        $logged = @($d.keyBurst4000Events.target -split '; ' | Where-Object { $_ -like 'key down*' }).Count
        $d.keyBurst4000Events.target = "$((($d.keyBurst4000Events.typed).Length)) letters in the text box and $logged key downs in the log, of 2000"
        $d.keyBurst4000Events.typed = ''
        $found.debounce = $d
    }

    if ($checks -contains 'scroll') {
        . C:\probe\scroll.ps1
        $s = [ordered]@{}
        $scrollTarget = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\scroll-target.ps1' -PassThru
        Start-Sleep -Seconds 8
        $psExe = 'C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe' # forward slashes: a backslash would need escaping in the JSON
        function ScrollSettings([string]$module) {
            '{"version":1,"app":{"islandEnabled":false},"modules":{"smooth-scrolling":' + $module + '},' +
            '"features":{"preset":"custom","installed":["smooth-scrolling"],"capabilitiesUsed":[]}}'
        }
        function Restart([string]$module) {
            Stop-Process -Id $script:chippi.Id -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 3
            $script:chippi = StartChippi -whole (ScrollSettings $module)
            [Probe]::MoveTo($x, $y)
        }
        function Pointer { [Probe]::MoveTo($x, $y); Start-Sleep -Milliseconds 300 }

        # Off: the same notches, untouched, for the baseline.
        Restart '{}'
        Pointer
        $s.off = [ordered]@{
            oneNotch = Measure-Scroll 'off oneNotch' { [Wheel]::Notches(1, 0, 120) }
            sixFast = Measure-Scroll 'off sixFast' { [Wheel]::Notches(6, 10, 120) }
        }

        Restart '{"smoothEnabled":true}'
        Pointer
        $s.smooth = [ordered]@{
            oneNotch = Measure-Scroll 'smooth oneNotch' { [Wheel]::Notches(1, 0, 120) }
            sixFast = Measure-Scroll 'smooth sixFast (10 ms apart)' { [Wheel]::Notches(6, 10, 120) }
            sixSlow = Measure-Scroll 'smooth sixSlow (300 ms apart)' { [Wheel]::Notches(6, 300, 120) } 2500
            reversal = Measure-Scroll 'smooth reversal (6 up, then 1 back)' { $t = [Wheel]::Notches(6, 10, 120); [Wheel]::Spin(60); [Wheel]::Send(-120, $false) | Out-Null; $t }
            ctrlWheel = Measure-Scroll 'smooth ctrl+wheel' { [Wheel]::Key(0x11, $true); $t = [Wheel]::Send(120, $false); [Wheel]::Key(0x11, $false); $t } 600
            horizontal = Measure-Scroll 'smooth horizontal' { [Wheel]::Send(120, $true) } 800
            fineDelta = Measure-Scroll 'smooth fine delta 30' { [Wheel]::Send(30, $false) } 600
        }
        # Move the pointer off the window 40 ms into a glide: nothing may reach it after the move.
        $moveAt = 0
        $s.smooth.switchWindow = Measure-Scroll 'smooth pointer leaves mid-glide' { $t = [Wheel]::Notches(8, 10, 120); [Wheel]::Spin(20); [Wheel]::Move(960, 740); $script:moveAt = [Wheel]::Now(); $t } 1500
        $csv = @(Get-Content -LiteralPath "$results\scroll.csv")
        $s.smooth.switchWindow.messagesAfterTheMove = @($csv | Where-Object { [double]($_ -split ',')[0] -gt ($moveAt + 25) }).Count
        Pointer
        # Ctrl goes down 40 ms into a glide, with no wheel event: the rest of the glide must not reach the window while Ctrl is held.
        $ctrlAt = 0
        $s.smooth.ctrlMidGlide = Measure-Scroll 'smooth ctrl pressed mid-glide' { $t = [Wheel]::Notches(8, 10, 120); [Wheel]::Spin(20); [Wheel]::Key(0x11, $true); $script:ctrlAt = [Wheel]::Now(); Start-Sleep -Milliseconds 800; [Wheel]::Key(0x11, $false); $t } 800
        $csv = @(Get-Content -LiteralPath "$results\scroll.csv")
        $s.smooth.ctrlMidGlide.messagesAfterCtrl = @($csv | Where-Object { [double]($_ -split ',')[0] -gt ($ctrlAt + 25) }).Count
        Pointer
        # A game in front pauses it: the notch arrives whole and a glide in progress ends. The target is topmost, over the game.
        # A long glide (about a second), so there is one still going when the switch to the game lands.
        Restart '{"smoothEnabled":true,"responseMs":400,"coastMs":400}'
        $game = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\target.ps1 -Fullscreen' -PassThru
        Start-Sleep -Seconds 8
        Pointer
        [Wheel]::Foreground('Scroll target') | Out-Null; Start-Sleep -Milliseconds 800
        $glideEnds = 0
        $s.smooth.pauseMidGlide = Measure-Scroll 'smooth game comes to the front mid-glide' { $t = [Wheel]::Notches(10, 10, 120); [Wheel]::Spin(30); [Wheel]::Key(0x12, $true); [Wheel]::Key(0x09, $true); [Wheel]::Key(0x09, $false); [Wheel]::Key(0x12, $false); $script:glideEnds = [Wheel]::Now(); $t } 2000
        $csv = @(Get-Content -LiteralPath "$results\scroll.csv")
        $s.smooth.pauseMidGlide.messagesMoreThan300msAfterTheSwitch = @($csv | Where-Object { [double]($_ -split ',')[0] -gt ($glideEnds + 300) }).Count
        Start-Sleep -Milliseconds 800
        $s.smooth.notchOverGame = Measure-Scroll 'smooth one notch with the game in front' { [Wheel]::Notches(1, 0, 120) } 800
        Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        [Wheel]::Foreground('Scroll target') | Out-Null

        Restart '{"linearEnabled":true}'
        Pointer
        $s.linear = [ordered]@{
            plainNotch = Measure-Scroll 'linear plain 120' { [Wheel]::Notches(1, 0, 120) } 800
            driverBig480 = Measure-Scroll 'linear driver message 480' { [Wheel]::Notches(1, 0, 480) } 800
        }

        Restart '{"smoothEnabled":true,"linearEnabled":true}'
        Pointer
        $s.both = [ordered]@{
            fast480x3 = Measure-Scroll 'both 3 x 480 (a fast spin)' { [Wheel]::Notches(3, 10, 480) }
            slow120x3 = Measure-Scroll 'both 3 x 120 (a slow spin)' { [Wheel]::Notches(3, 300, 120) } 2500
        }

        Restart ('{"smoothEnabled":true,"apps":[{"app":"' + $psExe + '","percent":0}]}')
        Pointer
        $s.appLeftAlone = [ordered]@{ oneNotch = Measure-Scroll 'app left alone: one notch' { [Wheel]::Notches(1, 0, 120) } 800 }

        Restart ('{"smoothEnabled":true,"apps":[{"app":"' + $psExe + '","percent":200}]}')
        Pointer
        $s.app200 = [ordered]@{ oneNotch = Measure-Scroll 'app at 200%: one notch' { [Wheel]::Notches(1, 0, 120) } }

        Stop-Process -Id $scrollTarget.Id -Force -ErrorAction SilentlyContinue
        $found.scroll = $s
    }

    if ($checks -contains 'shortcuts' -or $checks -contains 'caps') {
        $sc = [ordered]@{}
        $actionLog = "$results\actions.log"
        function Actions { if (Test-Path -LiteralPath $actionLog) { @(Get-Content -LiteralPath $actionLog) } else { @() } }
        function Door([string]$name, [scriptblock]$act) {
            Remove-Item -LiteralPath $actionLog, "$results\target.log" -ErrorAction SilentlyContinue
            [Probe]::MoveTo($x, $y)
            Start-Sleep -Milliseconds 300
            $before = [Probe]::Foreground()
            $capsBefore = [Probe]::CapsOn()
            $hitsBefore = [Probe]::Hits
            & $act
            Start-Sleep -Milliseconds 1500
            $sc[$name] = [ordered]@{ actions = ((Actions) -join '; '); targetHeard = ((TargetLog) -join '; '); foreground = "$before -> $([Probe]::Foreground())"; otherAppHotkeyHeard = [Probe]::Hits - $hitsBefore; capsBefore = $capsBefore; capsAfter = [Probe]::CapsOn() }
            Note "shortcuts $name : fired [$((Actions) -join '; ')] target heard [$((TargetLog) -join '; ')] other app heard $([Probe]::Hits - $hitsBefore) foreground $($sc[$name].foreground)"
            Start-Sleep -Milliseconds 300
        }
        if ($checks -contains 'caps') {
            Door 'capsBoundK' { [Probe]::Key(0x14, $true); [Probe]::Key(0x4B, $true); [Probe]::Key(0x4B, $true); [Probe]::Key(0x4B, $false); [Probe]::Key(0x14, $false) }
            Door 'capsUnboundM' { [Probe]::Key(0x14, $true); [Probe]::Key(0x4D, $true); [Probe]::Key(0x4D, $false); [Probe]::Key(0x14, $false) }
            Door 'capsSoloTap' { [Probe]::Key(0x14, $true); [Probe]::Key(0x14, $false) }
            Door 'capsSoloHold' { [Probe]::Key(0x14, $true); Start-Sleep -Milliseconds 600; [Probe]::Key(0x14, $false) }
        }
        if ($checks -contains 'caps') { $found.caps = $sc }
        else {
        Door 'backClick' { [Probe]::Back($true); Start-Sleep -Milliseconds 80; [Probe]::Back($false) }
        Door 'forwardClick' { [Probe]::Forward($true); Start-Sleep -Milliseconds 80; [Probe]::Forward($false) }
        Door 'middleClick' { [Probe]::Middle($true); Start-Sleep -Milliseconds 80; [Probe]::Middle($false) }
        Door 'backHeldHalfSecond' { [Probe]::Back($true); Start-Sleep -Milliseconds 600; [Probe]::Back($false) }
        Door 'leftClickIsNeverBound' { [Probe]::Left($true); Start-Sleep -Milliseconds 80; [Probe]::Left($false) }
        Door 'tiltRightOneNotch' { [Probe]::Tilt(120) }
        Door 'tiltLeftTwoNotches' { [Probe]::Tilt(-120); Start-Sleep -Milliseconds 60; [Probe]::Tilt(-120) }
        Door 'tiltRightFineWheel4x30' { 1..4 | ForEach-Object { [Probe]::Tilt(30); Start-Sleep -Milliseconds 20 } }
        Door 'ctrlAltK' { [Probe]::Key(0xA2, $true); [Probe]::Key(0xA4, $true); [Probe]::Key(0x4B, $true); [Probe]::Key(0x4B, $false); [Probe]::Key(0xA4, $false); [Probe]::Key(0xA2, $false) }
        Door 'winJ' { [Probe]::Key(0x5B, $true); [Probe]::Key(0x4A, $true); [Probe]::Key(0x4A, $false); [Probe]::Key(0x5B, $false) }
        Door 'winYHeldByAnotherApp' { [Probe]::Key(0x5B, $true); [Probe]::Key(0x59, $true); Start-Sleep -Milliseconds 700; [Probe]::Key(0x59, $true); [Probe]::Key(0x59, $false); [Probe]::Key(0x5B, $false) }
        Door 'plainKIsUntouched' { [Probe]::Key(0x4B, $true); [Probe]::Key(0x4B, $false) }
        # Win alone must still open Start: the masking is only for a chord Chippi kept.
        Door 'winAloneOpensStart' { [Probe]::Key(0x5B, $true); [Probe]::Key(0x5B, $false) }
        if ([Probe]::Foreground() -ne 'Probe target') { [Probe]::Key(0x1B, $true); [Probe]::Key(0x1B, $false); Start-Sleep -Milliseconds 500; [Probe]::MoveTo($x, $y); [Probe]::Left($true); [Probe]::Left($false); Start-Sleep -Milliseconds 500 }
        $found.shortcuts = $sc

        # The same buttons in an app on the list: the press and the tilt reach the app, nothing fires. Keys are unchanged.
        $sc = [ordered]@{}
        Stop-Process -Id $chippi.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 3
        $chippi = StartChippi -whole (ShortcutSettings '"C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"')
        Door 'inListedApp_backClick' { [Probe]::Back($true); Start-Sleep -Milliseconds 80; [Probe]::Back($false) }
        Door 'inListedApp_tiltRight' { [Probe]::Tilt(120) }
        Door 'inListedApp_ctrlAltK' { [Probe]::Key(0xA2, $true); [Probe]::Key(0xA4, $true); [Probe]::Key(0x4B, $true); [Probe]::Key(0x4B, $false); [Probe]::Key(0xA4, $false); [Probe]::Key(0xA2, $false) }
        $found.shortcutsInListedApp = $sc
        }
    }


    if ($checks -contains 'editor') {
        # Settings > Shortcuts driven through UI Automation, with real input into the hooks: pick an action from the dropdown,
        # record keys and a mouse button, read the refusal line, Save, then press the saved keys and see the action run.
        Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
        $ed = [ordered]@{}
        $actionLog = "$results\actions.log"
        $uia = [System.Windows.Automation.AutomationElement]
        function Shot([string]$name) {
            $b = New-Object System.Drawing.Bitmap ([System.Windows.Forms.SystemInformation]::VirtualScreen.Width), ([System.Windows.Forms.SystemInformation]::VirtualScreen.Height)
            $g = [System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen(0, 0, 0, 0, $b.Size); $g.Dispose()
            $b.Save("$results\editor-$name.png"); $b.Dispose()
        }
        function Named([string]$name, $root = $uia::RootElement) {
            $cond = New-Object System.Windows.Automation.PropertyCondition ($uia::NameProperty), $name
            $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        }
        function WaitNamed([string]$name, [int]$seconds = 10) {
            $end = (Get-Date).AddSeconds($seconds)
            while ((Get-Date) -lt $end) { $e = Named $name; if ($e) { return $e }; Start-Sleep -Milliseconds 250 }
            Shot 'missing'
            $win = Named 'Chippi Settings'
            if ($win) { Note ('names: ' + ((@($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })) -join ' | ')) }
            throw "No element named '$name'"
        }
        function Press($e) {
            $p = $null
            if ($e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return }
            if ($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); return }
            if ($e.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$p)) { $p.Toggle(); return }
            throw "Can't press $($e.Current.Name)"
        }
        function SetText($e, [string]$text) { $e.SetFocus(); $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text) }
        function Texts($window) {
            $cond = New-Object System.Windows.Automation.PropertyCondition ($uia::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Text)
            @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) | ForEach-Object { $_.Current.Name })
        }
        function Chord([uint16[]]$keys) {
            foreach ($k in $keys) { [Probe]::Key($k, $true); Start-Sleep -Milliseconds 30 }
            [array]::Reverse($keys)
            foreach ($k in $keys) { [Probe]::Key($k, $false); Start-Sleep -Milliseconds 30 }
            Start-Sleep -Milliseconds 700
        }
        function Line($window) { (Texts $window | Where-Object { $_ -match 'uses this|Pick a|failed' }) -join '; ' }
        function Field($window) { [ordered]@{ line = (Line $window); saveEnabled = (Named 'Save' $window).Current.IsEnabled } }

        Start-Process -FilePath 'C:\chippi\Chippi.exe' -ArgumentList '--dev-profile sandbox'
        $window = WaitNamed 'Chippi Settings' 20
        Start-Sleep -Seconds 2
        # The sidebar's items are named by their view model ("NavItem { Id = shortcuts, ..."): find that one and select it.
        $nav = $null
        $end = (Get-Date).AddSeconds(15)
        while (-not $nav -and (Get-Date) -lt $end) {
            $nav = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
                Where-Object { $_.Current.Name -like 'NavItem { Id = shortcuts,*' } | Select-Object -First 1
            if (-not $nav) { Start-Sleep -Milliseconds 300 }
        }
        Press $nav
        Start-Sleep -Seconds 1
        $add = WaitNamed 'Add shortcut' 10
        Shot 'list'
        $ed.listTexts = (Texts $window) -join ' | '
        Press $add
        Press (WaitNamed 'Choose an action' 10)
        $search = WaitNamed 'Search actions' 10
        Start-Sleep -Milliseconds 500
        Shot 'dropdown-open'
        SetText $search 'snap'
        Start-Sleep -Milliseconds 600
        Shot 'dropdown-search'
        $ed.searchHasSnapRight = [bool](Named 'Snap right')
        $ed.searchHidesKeepOnTop = -not (Named 'Keep on top')
        Press (Named 'Snap right')
        Start-Sleep -Milliseconds 500

        # 1. A clash with a saved binding is refused with one line, and Save stays off.
        Chord @(0xA2, 0xA4, 0x4B)
        $ed.clashSaved = Field $window
        Shot 'refused-saved'
        # 2. Another app holds Win+Y.
        Press (Named 'Shortcut' $window)
        Start-Sleep -Milliseconds 400
        Chord @(0x5B, 0x59)
        $ed.anotherApp = Field $window
        Shot 'refused-other-app'
        # 3. A free chord: no line, Save on; Save it.
        Press (Named 'Shortcut' $window)
        Start-Sleep -Milliseconds 400
        Chord @(0xA2, 0xA4, 0x4A)
        $ed.free = Field $window
        Shot 'recorded'
        Press (Named 'Save' $window)
        Start-Sleep -Milliseconds 800
        Shot 'saved'
        $ed.savedTexts = (Texts $window) -join ' | '
        $ed.settingsJson = (Get-Content -LiteralPath (Join-Path $profileDir 'settings.json') -Raw)
        # 4. The saved keys run the action through the host (the probe's log stands in for the real runner).
        Remove-Item -LiteralPath $actionLog -ErrorAction SilentlyContinue
        Chord @(0xA2, 0xA4, 0x4A)
        Start-Sleep -Milliseconds 800
        $ed.savedKeysFired = if (Test-Path -LiteralPath $actionLog) { (Get-Content -LiteralPath $actionLog) -join '; ' } else { '' }
        # 5. A mouse button: Middle is held by the Radial menu.
        Press (Named 'Add shortcut' $window)
        Start-Sleep -Milliseconds 500
        Press (WaitNamed 'Choose an action' 10)
        $null = WaitNamed 'Search actions' 10
        Press (WaitNamed 'Keep on top' 10)
        Start-Sleep -Milliseconds 400
        Press (WaitNamed 'Mouse button' 10)
        Start-Sleep -Milliseconds 600
        [Probe]::Middle($true); Start-Sleep -Milliseconds 80; [Probe]::Middle($false)
        Start-Sleep -Milliseconds 700
        $ed.radialMiddle = Field $window
        Shot 'refused-radial'
        Press (Named 'Shortcut' $window)
        Start-Sleep -Milliseconds 400
        [Probe]::Back($true); Start-Sleep -Milliseconds 80; [Probe]::Back($false)
        Start-Sleep -Milliseconds 700
        $ed.backButton = Field $window
        Press (Named 'Save' $window)
        Start-Sleep -Milliseconds 800
        Shot 'saved-mouse'
        # 6. Caps Lock combos are off: the held key isn't the combo's key. Caps then M records M, not Caps Lock itself.
        Press (Named 'Add shortcut' $window)
        Start-Sleep -Milliseconds 500
        Press (WaitNamed 'Choose an action' 10)
        $null = WaitNamed 'Search actions' 10
        Press (WaitNamed 'Keep on top' 10)
        Start-Sleep -Milliseconds 400
        Press (WaitNamed 'Caps Lock' 10)
        Start-Sleep -Milliseconds 600
        Chord @(0x14)
        Chord @(0x4D)
        $ed.capsRecorded = (Texts $window | Where-Object { $_ -in @('Caps Lock', 'M') }) -join ','
        Shot 'caps-recorded'
        Press (Named 'Cancel' $window)
        Start-Sleep -Milliseconds 500
        # 7. An Acts as chip toggled through UI Automation (a Toggle, no Click) is saved.
        $alt = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition ($uia::NameProperty), 'Alt')) |
            Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' } | Select-Object -First 1
        Press $alt
        Start-Sleep -Milliseconds 800
        $ed.capsModifiersAfterAltToggle = ([regex]::Match((Get-Content -LiteralPath (Join-Path $profileDir 'settings.json') -Raw), '"capsModifiers":\s*"[^"]*"')).Value
        $found.editor = $ed
    }

    if ($checks -contains 'snippets') {
        $sn = [ordered]@{}
        $vks = @{ ';' = 0xBA; ' ' = 0x20 }
        function TypeKeys([string]$keys) {
            foreach ($c in $keys.ToCharArray()) {
                $vk = if ($vks.ContainsKey([string]$c)) { $vks[[string]$c] } else { [int][char]::ToUpperInvariant($c) }
                [Probe]::Key([uint16]$vk, $true); [Probe]::Spin(25); [Probe]::Key([uint16]$vk, $false); [Probe]::Spin(25)
            }
        }
        # Types, then waits until the target's text stops changing (its handlers are slow PowerShell); returns what was added.
        function Typed([string]$name, [scriptblock]$act, [string]$file = 'target-text.txt') {
            $before = if (Test-Path "$results\$file") { (Get-Content -LiteralPath "$results\$file" -Raw -Encoding UTF8).Length } else { 0 }
            & $act
            Start-Sleep -Milliseconds 600
            $stable = 0; $last = -1
            for ($wait = 0; $wait -lt 80 -and $stable -lt 3; $wait++) {
                Start-Sleep -Milliseconds 300
                $now = (Get-Item -LiteralPath "$results\$file" -ErrorAction SilentlyContinue).Length
                if ($now -eq $last) { $stable++ } else { $stable = 0; $last = $now }
            }
            $all = if (Test-Path "$results\$file") { Get-Content -LiteralPath "$results\$file" -Raw -Encoding UTF8 } else { '' }
            $typed = $all.Substring([math]::Min($before, $all.Length))
            $sn[$name] = ($typed -replace "`r", '\r' -replace "`n", '\n')
            Note "snippets $name : '$($sn[$name])'"
        }
        Set-Clipboard -Value 'SENTINEL'
        [Probe]::MoveTo($x, $y)
        Typed 'rightAway;em' { TypeKeys ';em' }
        Typed 'afterSpace;go' { TypeKeys ';go'; TypeKeys ' ' }
        Typed 'afterEnter;go' { TypeKeys ';go'; [Probe]::Key(0x0D, $true); [Probe]::Spin(25); [Probe]::Key(0x0D, $false) }
        Typed 'accentEmojiNewline;ln' { TypeKeys ';ln' }
        Typed 'long200;lg' { TypeKeys ';lg' }
        Typed 'clipboardVariable;cp' { TypeKeys ';cp' }
        $sn.clipboardAfter = [string](Get-Clipboard)
        Typed 'noMatchTypedAsIs' { TypeKeys 'hello;ex' }
        Typed 'backspaceThenTrigger' { TypeKeys ';ex'; [Probe]::Key(0x08, $true); [Probe]::Spin(25); [Probe]::Key(0x08, $false); [Probe]::Spin(25); TypeKeys 'm' }
        # Keys typed the moment a trigger completes: the expansion is dropped or lands whole, and the typed keys are never deleted.
        # Either ";cpabc" (dropped) or "[SENTINEL]abc" (typed first) is right; "abc" alone or a cut-up text is not.
        Typed 'typedRightAfterTrigger;cp' { TypeKeys ';c'; [Probe]::Key(0x50, $true); [Probe]::Key(0x50, $false); foreach ($k in 0x41, 0x42, 0x43) { [Probe]::Key([uint16]$k, $true); [Probe]::Key([uint16]$k, $false) } }
        # Ctrl still down when the trigger completes, held past the one-second wait: nothing is typed and nothing is deleted.
        Typed 'ctrlHeldPastTheWait;em' { TypeKeys ';e'; [Probe]::Key(0x4D, $true); [Probe]::Key(0x4D, $false); [Probe]::Key(0x11, $true); Start-Sleep -Milliseconds 2500; [Probe]::Key(0x11, $false) }
        Typed 'clickBreaksATrigger' { TypeKeys ';e'; [Probe]::Left($true); [Probe]::Left($false); [Probe]::Spin(100); TypeKeys 'm' }
        # A password box: where the app tells UI Automation so, nothing expands.
        $password = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\password-target.ps1' -PassThru
        Start-Sleep -Seconds 5
        [Probe]::MoveTo($x, 120); [Probe]::Left($true); [Probe]::Left($false)
        Start-Sleep -Milliseconds 500
        $sn.foregroundWhileInPassword = [Probe]::Foreground()
        Typed 'passwordBoxGetsNothing;em' { TypeKeys ';em' } 'password-text.txt'
        Stop-Process -Id $password.Id -Force -ErrorAction SilentlyContinue
        $found.snippets = $sn
    }

    if ($checks -contains 'switcher') {
        # The App switcher between two windows: opened by its shortcut and by a Radial slice (a hub door, which needs the
        # overlay's one tap and retry for the foreground); Enter, Esc, search and an arrow key. Each step times the overlay
        # taking the foreground and the chosen window getting it, then types a letter to see where focus really landed.
        Add-Type -AssemblyName System.Drawing, System.Windows.Forms
        $sw = [ordered]@{}
        $other = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\probe\target.ps1 -Other' -PassThru
        Start-Sleep -Seconds 5
        function SwShot([string]$name) {
            $b = New-Object System.Drawing.Bitmap ([System.Windows.Forms.SystemInformation]::VirtualScreen.Width), ([System.Windows.Forms.SystemInformation]::VirtualScreen.Height)
            $g = [System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen(0, 0, 0, 0, $b.Size); $g.Dispose()
            $b.Save("$results\switcher-$name.png"); $b.Dispose()
        }
        function ReadText([string]$file) { if (Test-Path "$results\$file") { Get-Content -LiteralPath "$results\$file" -Raw -Encoding UTF8 } else { '' } }
        function Tap([uint16]$vk) { [Probe]::Key($vk, $true); [Probe]::Spin(25); [Probe]::Key($vk, $false); [Probe]::Spin(25) }
        function ClickOther { [Probe]::MoveTo(600, 70); [Probe]::Left($true); [Probe]::Left($false); Start-Sleep -Milliseconds 400 }
        function ClickTarget { [Probe]::MoveTo($x, $y); [Probe]::Left($true); [Probe]::Left($false); Start-Sleep -Milliseconds 400 }
        # Ctrl+Alt+J; returns the ms from J down to the overlay in front.
        function ByShortcut {
            [Probe]::Key(0xA2, $true); [Probe]::Key(0xA4, $true); [Probe]::Key(0x4A, $true)
            $ms = [Probe]::WaitForeground('App switcher', 3000)
            [Probe]::Key(0x4A, $false); [Probe]::Key(0xA4, $false); [Probe]::Key(0xA2, $false)
            return $ms
        }
        # Middle held, flicked up to the Switcher slice and let go; returns the ms from the release to the overlay in front.
        function ByRadial {
            [Probe]::Middle($true); Start-Sleep -Milliseconds 400
            [Probe]::Glide($x, $y, $x, $y - 150, 120); Start-Sleep -Milliseconds 150
            [Probe]::Middle($false)
            $ms = [Probe]::WaitForeground('App switcher', 3000)
            [Probe]::MoveTo($x, $y)
            return $ms
        }
        # One step: $open opens it, $keys runs inside it; $expect is the window that should end up in front, and $letter
        # (a virtual key) is typed there afterwards.
        function Step([string]$name, [scriptblock]$open, [scriptblock]$keys, [string]$expect, [uint16]$letter) {
            Remove-Item -LiteralPath "$results\target.log", "$results\other.log" -ErrorAction SilentlyContinue
            $before = [Probe]::Foreground()
            $targetText = ReadText 'target-text.txt'; $otherText = ReadText 'other-text.txt'
            $openMs = & $open
            Start-Sleep -Milliseconds 700
            SwShot $name
            $clock = [Diagnostics.Stopwatch]::StartNew()
            & $keys
            $switchMs = [Probe]::WaitForeground($expect, 3000)
            $after = [Probe]::Foreground()
            Start-Sleep -Milliseconds 300
            Tap $letter
            Start-Sleep -Milliseconds 800
            $sw[$name] = [ordered]@{
                foreground = "$before -> $after"; overlayInFrontMs = $openMs; chosenInFrontMs = $switchMs
                targetTyped = (ReadText 'target-text.txt').Substring($targetText.Length); otherTyped = (ReadText 'other-text.txt').Substring($otherText.Length)
                targetHeard = ((TargetLog) -join '; '); otherHeard = ((ReadText 'other.log') -replace "`r?`n", '; ')
            }
            Note "switcher $name : $($sw[$name] | ConvertTo-Json -Compress)"
            Start-Sleep -Milliseconds 500
        }
        # The order Chippi has seen: Other, then Target in front. The overlay selects the window before the one in front.
        ClickOther; ClickTarget
        Step 'shortcutEnter' { ByShortcut } { Tap 0x0D } 'Probe other' 0x51          # q into Other
        Step 'shortcutEsc' { ByShortcut } { Tap 0x1B } 'Probe other' 0x57            # Esc: back to Other, w into it
        Step 'searchTarget' { ByShortcut } { foreach ($k in 0x54, 0x41, 0x52, 0x47) { Tap ([uint16]$k) }; Tap 0x0D } 'Probe target' 0x45   # "targ", e into Target
        Step 'leftThenEnter' { ByShortcut } { Tap 0x25; Tap 0x0D } 'Probe target' 0x4C   # Left from Other to Target itself, l into it
        Step 'radialEnter' { ByRadial } { Tap 0x0D } 'Probe other' 0x52              # the hub door: r into Other
        Step 'radialEsc' { ByRadial } { Tap 0x1B } 'Probe other' 0x59                # y into Other
        # Opened five times more by the shortcut, for the timing spread.
        $opens = @()
        foreach ($i in 1..5) { ClickTarget; $opens += ByShortcut; Start-Sleep -Milliseconds 500; Tap 0x1B; Start-Sleep -Milliseconds 500 }
        $sw.shortcutOpensMs = $opens -join ', '
        $found.switcher = $sw
        Stop-Process -Id $other.Id -Force -ErrorAction SilentlyContinue
    }

    if ($checks -contains 'cost') {
        $cost = @()
        for ($run = 1; $run -le 3; $run++) {
            $chippi.Refresh()
            $before = $chippi.TotalProcessorTime.TotalMilliseconds
            $moves = [Probe]::Circle($x, $y, 150, 60000)
            $chippi.Refresh()
            $cpu = $chippi.TotalProcessorTime.TotalMilliseconds - $before
            Note "cost run ${run}: $moves moves, $cpu ms CPU"
            $cost += [ordered]@{ run = $run; moves = $moves; cpuMs = [math]::Round($cpu, 1) }
            Start-Sleep -Seconds 5
        }
        $found.cost = $cost
    }

    # The hub writes its timings every 10 s while events come.
    Start-Sleep -Seconds 12
    $logs = Join-Path $env:LOCALAPPDATA 'Chippi-dev\sandbox\logs'
    if (Test-Path $logs) { Copy-Item -Path $logs -Destination "$results\chippi-logs" -Recurse }
    $found.hubLog = @(Get-ChildItem "$results\chippi-logs" -Filter *.log -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content $_.FullName } | Where-Object { $_ -match 'Input hub' } | ForEach-Object { [string]$_ })
    $found.snippetLog = @(Get-ChildItem "$results\chippi-logs" -Filter *.log -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content $_.FullName } | Where-Object { $_ -match 'Text snippets' } | ForEach-Object { [string]$_ })
    if ($checks -contains 'switcher') {
        $found.switcherLog = @(Get-ChildItem "$results\chippi-logs" -Filter *.log -ErrorAction SilentlyContinue |
            ForEach-Object { Get-Content $_.FullName } | Where-Object { $_ -match 'app-switcher|App switcher|WRN|ERR' } | ForEach-Object { [string]$_ })
    }
    Stop-Process -Id $chippi.Id -Force -ErrorAction SilentlyContinue
}
catch {
    $found.error = $_.ToString()
    $found.errorLocation = $_.ScriptStackTrace
    $failLogs = Join-Path $env:LOCALAPPDATA 'Chippi-dev\sandbox\logs'
    if (Test-Path $failLogs) { Copy-Item -Path $failLogs -Destination "$results\chippi-logs" -Recurse -ErrorAction SilentlyContinue }
    Note "failed: $_"
}
finally {
    $found | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$results\results.json" -Encoding UTF8
    Set-Content -LiteralPath "$results\done.txt" -Value 'done'
    shutdown.exe /s /t 5
}
