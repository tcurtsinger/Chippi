// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Capture;
using Chippi.Core.Shortcuts;
using Chippi.UI.Capture;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.App.Shell;

/// <summary>
/// The capture layer's Sandbox check (tests/Chippi.Input.Tests/Sandbox/capture.ps1), before any capture feature exists:
/// with CHIPPI_CAPTURE_PROBE naming a log, Debug builds give each mode a shortcut, stand in for a feature (two plain bars,
/// Enter finishes, Esc cancels) and write what the layer did, one JSON object a line. Never in Release.
/// </summary>
internal sealed class CaptureProbe
{
    private readonly ShellContext _shell;
    private readonly string _log;
    private readonly List<IHotkey> _keys = [];
    private readonly IDisposable _use;

    private CaptureProbe(ShellContext shell, string log)
    {
        _shell = shell;
        _log = log;
        _use = shell.Capture.Use();
        // Print Screen as Screen capture's Capture region will be: the service turns Windows' setting off.
        Add("Capture region", new KeyShortcut(0, KeyShortcut.PrintScreen), new CaptureRequest(CaptureMode.Region));
        CaptureMode[] modes = [CaptureMode.Region, CaptureMode.Window, CaptureMode.Screen, CaptureMode.AllDisplays, CaptureMode.Freeform, CaptureMode.LastRegion, CaptureMode.PickPoint];
        for (int i = 0; i < modes.Length; i++)
            Add($"Probe {modes[i]}", Keys((uint)(0x31 + i)), new CaptureRequest(modes[i]) { Dim = modes[i] != CaptureMode.PickPoint });
        Add("Probe delay", Keys(0x38), new CaptureRequest(CaptureMode.Region) { Delay = 3 });
        Add("Probe swallowed", Keys(0x39), new CaptureRequest(CaptureMode.Region) { Door = CaptureDoor.Swallowed });
        shell.Shortcuts.Changed += () => Write("shortcuts", ("entries", string.Join("; ", shell.Shortcuts.List.Select(e => $"{e.Name}={e.Shortcut}|{e.Error}|held={e.Held}"))));
        Write("ready", ("setting", shell.PrintScreen.Read()?.ToString(CultureInfo.InvariantCulture) ?? "unset"),
            ("entries", string.Join("; ", shell.Shortcuts.List.Select(e => $"{e.Name}={e.Shortcut}|{e.Error}|held={e.Held}"))));
    }

    public static CaptureProbe? StartIfAsked(ShellContext shell) =>
        Environment.GetEnvironmentVariable("CHIPPI_CAPTURE_PROBE") is { Length: > 0 } log ? new CaptureProbe(shell, log) : null;

    private static KeyShortcut Keys(uint key) => new(ShortcutModifiers.Control | ShortcutModifiers.Alt, key);

    private void Add(string name, KeyShortcut keys, CaptureRequest request)
    {
        var hotkey = _shell.Shortcuts.Add("capture-probe", name);
        if (!hotkey.Register(keys, () => Start(name, request)))
            Write("refused", ("name", name), ("error", hotkey.Error));
        _keys.Add(hotkey);
    }

    private void Start(string name, CaptureRequest request)
    {
        var session = _shell.Capture.Start(request);
        Write("start", ("name", name), ("mode", request.Mode.ToString()), ("started", session is not null));
        if (session is null)
            return;
        session.Frozen += () =>
        {
            var freeze = session.Freeze!;
            var points = (Environment.GetEnvironmentVariable("CHIPPI_CAPTURE_PROBE_POINTS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split(',')).Select(p => new PixelPoint(int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture)));
            Write("frozen", ("name", name), ("displays", string.Join("; ", freeze.Screens.Select(s => $"{s.Bounds} dpi {s.Dpi} {s.DisplayId}"))),
                ("windows", string.Join("; ", freeze.Windows.Take(6).Select(w => $"{w.Title} {w.Frame}"))),
                ("pixels", string.Join("; ", points.Select(p => $"{p.X},{p.Y}=#{(freeze.PixelAt(p) ?? 0) & 0xFFFFFF:X6}"))),
                ("foreground", Foreground()), ("selection", session.Selection?.Rect.ToString()));
        };
        session.Selected += s =>
        {
            Write("selected", ("name", name), ("rect", s.Rect.ToString()), ("window", s.Window?.Title), ("fastPath", s.FastPath),
                ("display", s.Display.Bounds.ToString()), ("mask", s.Mask is not null), ("cut", $"{s.Image().Width}x{s.Image().Height}"));
            session.SetBars(Bar(40, 200, true), Bar(200, 40, false));
        };
        session.Picked += (p, color) =>
        {
            Write("picked", ("name", name), ("point", $"{p.X},{p.Y}"), ("color", $"#{color & 0xFFFFFF:X6}"));
            session.Close(CaptureEnd.Finished);
        };
        session.KeyDown += e =>
        {
            Write("key", ("name", name), ("key", e.Key.ToString()), ("modifiers", Keyboard.Modifiers.ToString()));
            if (e.Key == Key.Enter)
                session.Close(CaptureEnd.Finished);
            else if (e.Key == Key.Escape)
                session.Close(CaptureEnd.Cancelled);
            e.Handled = true;
        };
        session.Closed += async end =>
        {
            Write("closed", ("name", name), ("end", end.ToString()));
            await Task.Delay(400);
            Write("after", ("name", name), ("foreground", Foreground()));
        };
    }

    private static Border Bar(double width, double height, bool vertical) => new()
    {
        Width = width,
        Height = height,
        CornerRadius = new CornerRadius(8),
        Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2E)),
        Child = new TextBlock { Text = vertical ? "T" : "Output", Foreground = Brushes.White, Margin = new Thickness(8) },
    };

    private static string Foreground()
    {
        nint hwnd = GetForegroundWindow();
        var title = new System.Text.StringBuilder(256);
        GetWindowText(hwnd, title, title.Capacity);
        return title.ToString();
    }

    private void Write(string name, params (string Key, object? Value)[] fields)
    {
        var line = new Dictionary<string, object?> { ["event"] = name, ["t"] = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) };
        foreach (var (key, value) in fields)
            line[key] = value;
        try { File.AppendAllText(_log, JsonSerializer.Serialize(line) + Environment.NewLine); }
        catch (IOException) { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, System.Text.StringBuilder text, int count);
}
#endif
