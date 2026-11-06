// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Capture;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.UI.Capture;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.Features.ScreenCapture;

public sealed class ScreenCaptureModule(ICaptureLayer capture) : IFeatureModule
{
    public const string Id = "screen-capture";
    private ModuleContext? _context;
    private RecentCaptureStore? _store;
    private IDisposable? _use;
    private readonly List<IHotkey> _keys = [];
    private CaptureEditor? _editor;
    private DispatcherTimer? _clear;
    public ScreenCaptureModel? Model { get; private set; }
    public FeatureDescriptor Descriptor { get; } = new(Id, "Screen capture", "Capture and mark up anything on screen", FeatureGroup.Capture)
    { Icon = "Scan", Presets = PresetMembership.PowerUser };

    private static readonly (string Name, CaptureMode Mode, string Command)[] Modes =
    [
        (ScreenCaptureShortcuts.CaptureRegion, CaptureMode.Region, "region"),
        (ScreenCaptureShortcuts.CaptureWindow, CaptureMode.Window, "window"),
        (ScreenCaptureShortcuts.CaptureFullScreen, CaptureMode.Screen, "screen"),
        (ScreenCaptureShortcuts.CaptureAllDisplays, CaptureMode.AllDisplays, "all-screens"),
        (ScreenCaptureShortcuts.CaptureFreeform, CaptureMode.Freeform, "freeform"),
        (ScreenCaptureShortcuts.CaptureLastRegion, CaptureMode.LastRegion, "repeat"),
    ];

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        _store = new RecentCaptureStore(Path.Combine(context.DataDirectory, "recent"));
        Model = new ScreenCaptureModel(context, _store, Annotate, CaptureSavedFile.Recycle);
        context.Ui.SetSettingsPage(_ => new Views.ScreenCaptureSettingsPage(Model));
        _use = capture.Use();
        foreach (var mode in Modes) AddShortcut(mode.Name, () => Start(mode.Mode, CaptureDoor.Shortcut));
        // Delay's picker belongs to the approved Capture panel, which the host opens.
        AddShortcut(ScreenCaptureShortcuts.CaptureAfterDelay, OpenCapture);
        AddShortcut("Open recent captures", ShowRecent);
        context.Ui.AddCommands(() =>
        [
            .. Modes.Select(mode => new ChippiCommand($"{Id}/{mode.Command}", mode.Name, "Scan",
                () => { Start(mode.Mode, CaptureDoor.Island); return Task.CompletedTask; })),
            new ChippiCommand($"{Id}/recent", "Open recent captures", "History", () => { ShowRecent(); return Task.CompletedTask; }),
            new ChippiCommand($"{Id}/delay", ScreenCaptureShortcuts.CaptureAfterDelay, "Timer", () => { OpenCapture(); return Task.CompletedTask; }),
        ]);
        return Task.CompletedTask;
    }

    private KeyShortcut? Saved(string name) => Model!.Settings.Shortcuts is { } keys
        ? keys.GetValueOrDefault(name)
        : name == ScreenCaptureShortcuts.CaptureRegion ? new KeyShortcut(0, KeyShortcut.PrintScreen) : null;

    private void AddShortcut(string name, Action pressed)
    {
        var context = _context!;
        IHotkey key = null!;
        string? Change(KeyShortcut? keys)
        {
            var old = Saved(name);
            if (keys != null && !key.Register(keys, pressed)) return key.Error;
            var saved = ScreenCaptureShortcuts.All.Where(x => x != ScreenCaptureShortcuts.OpenCapture)
                .Append("Open recent captures").ToDictionary(x => x, Saved);
            saved[name] = keys;
            if (!Model!.Save(Model.Settings with { Shortcuts = saved }))
            {
                if (old != null) key.Register(old, pressed); else key.Unregister();
                return "Save failed";
            }
            if (keys == null) key.Unregister();
            return null;
        }
        key = context.Shortcuts.Add(Id, name, new ShortcutEdits(() => Saved(name), keys => Change(keys), () => Change(null)) { Icon = "Scan" });
        _keys.Add(key);
        if (Saved(name) is { } initial) key.Register(initial, pressed);
    }

    public void Start(CaptureMode mode, CaptureDoor door, int delay = 0)
    {
        if (Model == null || _context == null) return;
        if (_editor != null) { _editor.AskDiscard(() => Start(mode, door, delay)); return; }
        Attach(capture.Start(new CaptureRequest(mode) { Door = door, Delay = delay, Magnifier = Model.Settings.Magnifier }));
    }

    private void Annotate(BitmapSource image)
    {
        if (_editor != null) { _editor.AskDiscard(() => Annotate(image)); return; }
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[checked(converted.PixelWidth * converted.PixelHeight * 4)];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        Attach(capture.Reopen(new CapturedImage(image.PixelWidth, image.PixelHeight, pixels), CaptureDoor.Island));
    }

    private void Attach(ICaptureSession? session)
    {
        if (session == null || Model == null || _context == null || _store == null) return;
        _editor = new CaptureEditor(session, _context, Model, _store, Feedback);
        session.Closed += _ => { _editor?.Dispose(); _editor = null; };
    }

    private void OpenCapture()
    {
        if (_context is not { } context) return;
        if (!context.TryOpenIslandPage("capture") && !context.SummonIslandPage("capture")) context.OpenSettings(null);
    }
    private void ShowRecent() => _context?.OpenSettings("recent");
    private void Feedback(string title, bool failed)
    {
        if (_context is not { } context) return;
        _clear?.Stop();
        context.Island.Publish(new IslandActivity(Id + "/result", IslandActivityKind.Status, title, "Screen capture")
        { IsFeedback = true, NeedsAttention = failed, Icon = failed ? "TriangleAlert" : null, Clicked = ShowRecent });
        _clear = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _clear.Tick += (_, _) => { _clear?.Stop(); context.Island.Clear(Id + "/result"); };
        _clear.Start();
    }
    public Task StopAsync(CancellationToken ct)
    {
        _clear?.Stop(); _clear = null;
        _editor?.Cancel(); _editor?.Dispose(); _editor = null;
        foreach (var key in _keys) key.Dispose();
        _keys.Clear();
        _use?.Dispose(); _use = null;
        Model?.Dispose(); Model = null;
        _store?.Dispose(); _store = null;
        _context = null;
        return Task.CompletedTask;
    }
}
