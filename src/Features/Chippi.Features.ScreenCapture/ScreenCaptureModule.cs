// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using Chippi.Capture;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Features.ScreenCapture.Scrolling;
using Chippi.Media;
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
    private ScrollingSession? _scrolling;
    public Action? OpenDelay { get; set; }

    /// <summary>
    /// Builds the controller and the stage of a scrolling capture: Windows Graphics Capture frames, the real scroll target and
    /// the shell's action PC, with the island's windows. Null when the shell has no action PC. Tests replace it with fakes.
    /// </summary>
    internal Func<ScrollingStart, ScrollingParts?>? ScrollingFactory { get; set; }

    /// <summary>A scrolling capture is on screen (its stage, not a frozen screen): a starter waits for it to end.</summary>
    internal bool IsScrolling => _scrolling is { IsEnded: false };
    public Func<bool> TextAvailable { get; init; } = () => false;
    public Func<CapturedImage, bool, Task> ReadText { get; init; } = (_, _) => Task.CompletedTask;
    public CaptureRecentModel? RecentModel { get; private set; }
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
        RecentModel = new CaptureRecentModel(Model, _store, context);
        context.Ui.SetSettingsPage(_ => new Views.ScreenCaptureSettingsPage(Model));
        _use = capture.Use();
        foreach (var mode in Modes) AddShortcut(mode.Name, () => Start(mode.Mode, CaptureDoor.Shortcut));
        // The selector, whose release goes straight to scrolling (scrolling-capture.md, Doors).
        AddShortcut(ScreenCaptureShortcuts.CaptureScrollingArea, () => Start(CaptureMode.Region, CaptureDoor.Shortcut, scrolling: true));
        // Delay's picker belongs to the approved Capture panel, which the host opens.
        AddShortcut(ScreenCaptureShortcuts.CaptureAfterDelay, () => OpenDelay?.Invoke());
        AddShortcut("Open recent captures", ShowRecent);
        context.Ui.AddCommands(() =>
        [
            .. Modes.Select(mode => new ChippiCommand($"{Id}/{mode.Command}", mode.Name, "Scan",
                () => { Start(mode.Mode, CaptureDoor.Island); return Task.CompletedTask; })),
            new ChippiCommand($"{Id}/scrolling", ScreenCaptureShortcuts.CaptureScrollingArea, "UnfoldVertical",
                () => { Start(CaptureMode.Region, CaptureDoor.Island, scrolling: true); return Task.CompletedTask; }),
            new ChippiCommand($"{Id}/recent", "Open recent captures", "History", () => { ShowRecent(); return Task.CompletedTask; }),
            new ChippiCommand($"{Id}/delay", ScreenCaptureShortcuts.CaptureAfterDelay, "Timer", () => { OpenDelay?.Invoke(); return Task.CompletedTask; }),
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

    public void Start(CaptureMode mode, CaptureDoor door, int delay = 0, bool scrolling = false)
    {
        if (Model == null || _context == null || IsScrolling) return;
        var request = new CaptureRequest(mode) { Door = door, Delay = delay, Magnifier = Model.Settings.Magnifier };
        if (_editor != null)
        {
            if (capture.Current?.Selection == null) { capture.Start(request); return; }
            _editor.AskDiscard(() => Start(mode, door, delay, scrolling)); return;
        }
        Attach(capture.Start(request), scrolling);
    }

    private void Annotate(BitmapSource image)
    {
        if (IsScrolling) return;
        if (_editor != null)
        {
            if (capture.Current?.Selection == null) { capture.Start(new CaptureRequest(CaptureMode.Region)); return; }
            _editor.AskDiscard(() => Annotate(image)); return;
        }
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[checked(converted.PixelWidth * converted.PixelHeight * 4)];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        Attach(capture.Reopen(new CapturedImage(image.PixelWidth, image.PixelHeight, pixels), CaptureDoor.Shortcut));
    }

    private void Attach(ICaptureSession? session, bool scrolling = false, bool scrollingResult = false, Task<BitmapSource>? scrollingBitmap = null)
    {
        if (session == null || !ReferenceEquals(capture.Current, session) || Model == null || _context == null || _store == null) return;
        _editor = new CaptureEditor(session, _context, Model, _store, Feedback, TextAvailable, ReadText)
        {
            StartScroll = scrollingResult || (_context.Actions == null && ScrollingFactory == null) ? null : StartScrolling,
            ScrollOnRelease = scrolling,
            ScrollingResult = scrollingResult,
            ScrollingBitmap = scrollingBitmap,
            ScrollKeys = () => Saved(ScreenCaptureShortcuts.CaptureScrollingArea)?.ToString(),
        };
        session.Closed += _ => { _editor?.Dispose(); _editor = null; };
    }

    /// <summary>Scroll was pressed (or released into): the stage goes up over the live screen and the frames start.</summary>
    private void StartScrolling(ScrollingStart start)
    {
        if (_context is not { } context || IsScrolling) return;
        ScrollingParts? parts = null;
        try { parts = (ScrollingFactory ?? DefaultScrolling)(start); }
        catch (Exception ex) { Chippi.Core.Diagnostics.Log.Warn("Scrolling capture: it couldn't be set up", ex); }
        if (parts == null) { Feedback("Scrolling capture failed", true); return; }
        _scrolling = new ScrollingSession(parts.Controller, parts.Stage, start.Request.Area.Size, Dispatcher.CurrentDispatcher, Scrolled) { Timings = ScrollTimings.Sink };
        _scrolling.Start();
    }

    private ScrollingParts? DefaultScrolling(ScrollingStart start)
    {
        if (_context?.Actions is not { } pc) return null;
        var controller = new ScrollingController(start.Request, new WgcAreaFrames(), new WindowsScrollTarget(), pc, new SystemScrollClock()) { Timings = ScrollTimings.Sink };
        return new ScrollingParts(controller, new ScrollingStage(new ScrollingStageSetup(start.Request.Area, start.Request.Display, start.Dpi, capture.BarStyles)));
    }

    /// <summary>The scrolling capture ended: Done opens the result on the area's display, a failure says so, Cancel keeps nothing.</summary>
    private void Scrolled(ScrollingOutcome outcome, CapturedImage? image)
    {
        long doneAt = _scrolling?.DoneAt ?? 0;
        _scrolling = null;
        if (Model == null || _context == null) return;
        if (outcome == ScrollingOutcome.Failed) { Feedback("Scrolling capture failed", true); return; }
        if (outcome != ScrollingOutcome.Done || image == null) return;
        // The bitmap the editor keeps is made on a worker while the screen freezes and the result is drawn into it, not after.
        var bitmap = Task.Run(() => CaptureEditor.Bitmap(image));
        long reopened = Stopwatch.GetTimestamp();
        var session = capture.Reopen(image, CaptureDoor.Shortcut);
        if (session == null) { Feedback("Scrolling capture failed", true); return; }
        if (ScrollTimings.Sink is { } report)
        {
            report("scroll-done reopen-call=" + ScrollTimings.Ms(reopened) + " size=" + image.Width + "x" + image.Height);
            session.Frozen += () => report("scroll-shown reopen-to-shown=" + ScrollTimings.Ms(reopened) + (doneAt != 0 ? " done-to-shown=" + ScrollTimings.Ms(doneAt) : ""));
        }
        Attach(session, scrollingResult: true, scrollingBitmap: bitmap);
    }

    public void RefreshProviders() => _editor?.RefreshProviders();

    private void OpenCapture()
    {
        if (_context is not { } context) return;
        if (!context.TryOpenIslandPage("capture") && !context.SummonIslandPage("capture")) context.OpenSettings(null);
    }
    private void ShowRecent() => _context?.OpenSettings("recent");

    /// <summary>A line on the island's bar for six seconds, as a capture's result shows ("Open folder failed").</summary>
    public void Report(string title, bool failed) => Feedback(title, failed);

    private void Feedback(string title, bool failed)
    {
        if (_context is not { } context) return;
        context.Island.Publish(new IslandActivity(Id + "/result", IslandActivityKind.Status, title, _editor?.FeedbackSource ?? "Screen capture")
        { IsFeedback = true, Picture = _editor?.FeedbackPicture, NeedsAttention = failed, Icon = failed ? "TriangleAlert" : null, Clicked = ShowRecent });
    }
    public Task StopAsync(CancellationToken ct)
    {
        _context?.Island.Clear(Id + "/result");
        _scrolling?.Dispose(); _scrolling = null;
        _editor?.Cancel(); _editor?.Dispose(); _editor = null;
        foreach (var key in _keys) key.Dispose();
        _keys.Clear();
        _use?.Dispose(); _use = null;
        RecentModel?.Dispose(); RecentModel = null;
        Model?.Dispose(); Model = null;
        _store?.Dispose(); _store = null;
        _context = null;
        return Task.CompletedTask;
    }
}
