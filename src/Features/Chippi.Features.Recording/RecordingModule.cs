// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.UI.Capture;

namespace Chippi.Features.Recording;

/// <summary>
/// Screen recording (docs/phase-7/screen-recording.md): Record region, window, screen and GIF on the capture layer's
/// record mode, Stop recording, the bar's live line, and the recording itself (<see cref="RecordingController"/>). Its
/// screens bind to <see cref="Recording"/>; the App puts them in the Capture panel.
/// </summary>
public sealed class RecordingModule(ICaptureLayer capture, Func<string, IRecordingPc>? pc = null) : IFeatureModule, IRecordingShell
{
    public const string Id = "screen-recording";
    private const string LiveKey = Id + "/live", FeedbackKey = Id + "/result";

    private readonly List<IHotkey> _keys = [];
    private ModuleContext? _context;
    private RecordingSettings _settings = new();
    private IDisposable? _use;
    private DispatcherTimer? _clear;
    private Dispatcher? _ui;

    public FeatureDescriptor Descriptor { get; } = new(Id, "Screen recording", "Record a region or window to video or GIF", FeatureGroup.Capture)
    { Icon = "Video", Requires = [Capability.ScreenCapture], Presets = PresetMembership.PowerUser };

    /// <summary>The recording, for every recording screen; null while the feature isn't running.</summary>
    public RecordingController? Recording { get; private set; }

    /// <summary>The PC it records on, while running.</summary>
    public IRecordingPc? Pc { get; private set; }

    public RecordingSettings Settings => _settings;

    /// <summary>Lumen's start bar under the selection (MP4 or GIF, System sound, Mic, Start, Cancel, then the count).</summary>
    public Func<RecordingController, FrameworkElement>? StartBar { get; set; }

    /// <summary>Opens the Capture panel on the recording page; false when it can't. Set by the App's Capture host.</summary>
    public Func<bool>? OpenPage { get; set; }

    /// <summary>A saved recording goes to Screen capture's Recent. Set by the App.</summary>
    public Action<string, BitmapSource?, TimeSpan, DateTimeOffset>? Recent { get; set; }

    /// <summary>A recording started or ended: the App leaves the puck out of captures only while one runs.</summary>
    public event Action<bool>? RecordingChanged;

    /// <summary>Settings, a shortcut or the recording changed.</summary>
    public event Action? Changed;

    private static readonly (string Name, RecordingSource Source, bool Gif, string Command)[] Starters =
    [
        (RecordingShortcuts.RecordRegion, RecordingSource.Region, false, "region"),
        (RecordingShortcuts.RecordWindow, RecordingSource.Window, false, "window"),
        (RecordingShortcuts.RecordScreen, RecordingSource.Screen, false, "screen"),
        (RecordingShortcuts.RecordGif, RecordingSource.Region, true, "gif"),
    ];

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        _ui = Dispatcher.CurrentDispatcher;
        _settings = context.Settings.Get<RecordingSettings>();
        string temp = Path.Combine(context.DataDirectory, "temp");
        if (pc is null)
        {
            var windows = new WindowsRecordingPc(temp);
            windows.ClearTemp();
            Pc = windows;
        }
        else
            Pc = pc(temp);
        Recording = new RecordingController(capture, Pc, this, () => _settings);
        Recording.BarsWanted += session => session.SetBars(null, StartBar?.Invoke(Recording));
        Recording.Changed += () => Changed?.Invoke();
        _use = capture.Use();
        foreach (var s in Starters)
            AddShortcut(s.Name, () => Begin(s.Source, s.Gif, CaptureDoor.Shortcut));
        AddShortcut(RecordingShortcuts.StopRecording, () => _ = Recording?.StopAsync());
        context.Shortcuts.Changed += RaiseChanged;
        context.Ui.AddCommands(() =>
        [
            .. Starters.Select(s => new ChippiCommand($"{Id}/{s.Command}", s.Name, "Video",
                () => { Begin(s.Source, s.Gif, CaptureDoor.Island); return Task.CompletedTask; }) { Words = "record recording video screencast gif" }),
            new ChippiCommand($"{Id}/stop", RecordingShortcuts.StopRecording, "Square", () => Recording?.StopAsync() ?? Task.CompletedTask) { Words = "record recording video end" },
        ]);
        return Task.CompletedTask;
    }

    /// <summary>A starter from any door: a shortcut, the Capture panel, Search or the Radial menu.</summary>
    public void Begin(RecordingSource source, bool gif, CaptureDoor door) => _ = Recording?.BeginAsync(source, gif, door);

    private void RaiseChanged() => Changed?.Invoke();

    private KeyShortcut? Saved(string name) => _settings.Shortcuts?.GetValueOrDefault(name);

    private void AddShortcut(string name, Action pressed)
    {
        var context = _context!;
        IHotkey key = null!;
        string? Change(KeyShortcut? keys)
        {
            var old = Saved(name);
            if (keys != null && !key.Register(keys, pressed)) return key.Error;
            var saved = RecordingShortcuts.All.ToDictionary(x => x, Saved);
            saved[name] = keys;
            if (!Save(_settings with { Shortcuts = saved }))
            {
                if (old != null) key.Register(old, pressed); else key.Unregister();
                return "Save failed";
            }
            if (keys == null) key.Unregister();
            return null;
        }
        key = context.Shortcuts.Add(Id, name, new ShortcutEdits(() => Saved(name), keys => Change(keys), () => Change(null)) { Icon = "Video" });
        _keys.Add(key);
        if (Saved(name) is { } initial) key.Register(initial, pressed);
    }

    /// <summary>Settings › Recording's rows write here.</summary>
    public bool Save(RecordingSettings settings)
    {
        if (_context is not { } context) return false;
        var before = _settings;
        _settings = settings;
        try { context.Settings.Save(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _settings = before;
            return false;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>A starter's or Stop's keys and why they didn't register, for Settings › Recording's read-only rows.</summary>
    public (KeyShortcut? Keys, string? Error) Shortcut(string name)
    {
        if (_context is not { } context) return (null, null);
        foreach (var entry in context.Shortcuts.List)
            if (entry.Owner == Id && entry.Name == name)
                return (entry.Shortcut, entry.Error);
        return (null, null);
    }

    /// <summary>A row's Edit: Settings › Shortcuts with the editor in that row.</summary>
    public void EditShortcut(string name) => _context?.EditShortcut(name);

    // ---- IRecordingShell ----

    void IRecordingShell.Post(Action action)
    {
        if (_ui is not { } ui) return;
        if (ui.CheckAccess()) action();
        else ui.BeginInvoke(action);
    }

    void IRecordingShell.Feedback(string title, string source, bool failed, Action? clicked)
    {
        if (_context is not { } context) return;
        _clear?.Stop();
        context.Island.Publish(new IslandActivity(FeedbackKey, IslandActivityKind.Status, title, source)
        { IsFeedback = true, NeedsAttention = failed, Icon = failed ? "TriangleAlert" : "Video", Clicked = clicked });
        _clear = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _clear.Tick += (_, _) => { _clear?.Stop(); context.Island.Clear(FeedbackKey); };
        _clear.Start();
    }

    void IRecordingShell.Live(string? title, string? source)
    {
        if (_context is not { } context) return;
        if (title is null)
            context.Island.Clear(LiveKey);
        else
            context.Island.Publish(new IslandActivity(LiveKey, IslandActivityKind.Recording, title, source ?? "") { OpensTile = "capture", Icon = "Video" });
    }

    bool IRecordingShell.OpenReview() => OpenPage?.Invoke() ?? false;

    void IRecordingShell.AddToRecent(string file, BitmapSource? poster, TimeSpan length, DateTimeOffset time) => Recent?.Invoke(file, poster, length, time);

    void IRecordingShell.Recording(bool on) => RecordingChanged?.Invoke(on);

    public async Task StopAsync(CancellationToken ct)
    {
        if (_context is { } context) context.Shortcuts.Changed -= RaiseChanged;
        foreach (var key in _keys) key.Dispose();
        _keys.Clear();
        if (Recording is { } recording)
        {
            await recording.ShutDownAsync();
            recording.Dispose();
        }
        Recording = null;
        _use?.Dispose(); _use = null;
        _clear?.Stop(); _clear = null;
        _context?.Island.Clear(LiveKey);
        _context = null;
        Changed?.Invoke();
    }

    public Task UninstallAsync(CancellationToken ct) => Task.CompletedTask;
}
