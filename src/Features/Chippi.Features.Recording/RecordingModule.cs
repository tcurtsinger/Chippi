// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Media;
using Microsoft.Win32;

namespace Chippi.Features.Recording;

public sealed class RecordingModule : IFeatureModule
{
    public const string Id = "screen-recording";
    public const string Page = "recording";
    public static readonly string[] ShortcutNames = ["Record window", "Record screen", "Pause or resume recording", "Stop recording"];
    private readonly List<IHotkey> _keys = [];
    private ModuleContext? _context;
    private IRecordingStore? _store;
    private RecordingPhase _publishedPhase;
    private IReadOnlyList<RecordingEntry>? _publishedRecent;
    private bool _stopping;
    public RecordingModel? Model { get; private set; }
    public Func<bool> CanStart { get; set; } = () => true;
    public event Action? Changed;
    public FeatureDescriptor Descriptor { get; } = new(Id, "Screen recording", "Record a window or screen with sound", FeatureGroup.Capture)
    { Icon = "Video", Presets = PresetMembership.PowerUser };

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); _context = context;
        _stopping = false;
        _store = new RecordingStore(Path.Combine(context.DataDirectory, "recent"));
        var model = Model = new RecordingModel(context, new WindowsRecordingBackend(), _store);
        model.CanStart = () => CanStart();
        model.Changed += Publish;
        model.Starting += context.CloseIsland;
        model.ResultReady += ResultReady;
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        context.Ui.AddIslandTile(new IslandTileSpec(Page, "Screen recording", "Video", 76, 0, () => new RecordingView(model, context))
        { OnHome = false, HandleBack = () => { context.TryGoBackIslandPage("capture"); return true; } });
        context.Ui.SetSettingsPage(() => new RecordingSettingsView(model, context));
        Action[] actions = [() => Setup(RecordingTarget.Window), () => Setup(RecordingTarget.Screen), model.TogglePause, () => _ = model.StopAsync()];
        for (int i = 0; i < actions.Length; i++) AddShortcut(ShortcutNames[i], actions[i]);
        context.Ui.AddCommands(() => actions.Select((action, i) => new ChippiCommand(Id + "/" + new[] { "window", "screen", "pause-resume", "stop" }[i], ShortcutNames[i], "Video", () => { action(); return Task.CompletedTask; })).ToArray());
        Changed?.Invoke();
        return Task.CompletedTask;
    }
    public void Setup(RecordingTarget target)
    {
        if (Model == null || _context == null) return;
        if (Model.Active) { Model.Report("Already recording"); return; }
        Model.Setup(target); OpenControls();
    }
    public void OpenControls()
    {
        if (_context == null) return;
        if (!_context.TryOpenIslandPage(Page)) _context.SummonIslandPage(Page);
    }
    public void Open(RecordingEntry entry) { Model?.Open(entry); _context?.TryOpenIslandPage(Page); }
    private void ResultReady()
    {
        if (_stopping || _context == null) return;
        _context.Island.Publish(new IslandActivity(Id + "/saved", IslandActivityKind.Status, "Saved", "Screen recording") { IsFeedback = true, Icon = "Video", OpensTile = Page });
        _context.TryOpenIslandPage(Page);
    }
    private void SessionSwitch(object sender, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionLock) Model?.Interrupt("Recording stopped because the computer was locked"); }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Suspend) Model?.Interrupt("Recording stopped because the computer is going to sleep"); }
    private void DisplayChanged(object? sender, EventArgs e) => Model?.Interrupt("Recording stopped because the display configuration changed");
    private void Publish()
    {
        if (Model is not { } model || _context is not { } context) return;
        if (model.Active)
        {
            string title = model.Phase switch { RecordingPhase.Countdown => $"Starting in {model.Countdown}", RecordingPhase.Starting => "Starting…", RecordingPhase.Stopping => "Saving…", _ => FormatTime(model.Elapsed) };
            context.Island.Publish(new IslandActivity(Id, IslandActivityKind.Recording, title, model.Source?.Name ?? "Screen recording")
            {
                OpensTile = Page,
                Recording = model.Phase is RecordingPhase.Recording or RecordingPhase.Paused or RecordingPhase.Stopping
                    ? new(model.IsPaused, model.Phase != RecordingPhase.Stopping, model.TogglePause, () => _ = model.StopAsync()) : null,
                Clicked = model.Phase is RecordingPhase.Countdown or RecordingPhase.Starting ? () => _ = model.StopAsync() : null,
            });
        }
        else context.Island.Clear(Id);
        if (_publishedPhase != model.Phase || !ReferenceEquals(_publishedRecent, model.Recent))
        { _publishedPhase = model.Phase; _publishedRecent = model.Recent; Changed?.Invoke(); }
    }
    public static string FormatTime(TimeSpan time) => time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    private void AddShortcut(string name, Action action)
    {
        var context = _context!; IHotkey key = null!;
        KeyShortcut? Saved() => Model!.Settings.Shortcuts.GetValueOrDefault(name);
        string? Change(KeyShortcut? shortcut)
        {
            var previous = Saved();
            if (shortcut != null && !key.Register(shortcut, action)) return key.Error;
            var keys = new Dictionary<string, KeyShortcut?>(Model!.Settings.Shortcuts) { [name] = shortcut };
            if (!Model.SaveSettings(Model.Settings with { Shortcuts = keys }))
            { if (previous != null) key.Register(previous, action); else key.Unregister(); return "Save failed"; }
            if (shortcut == null) key.Unregister(); return null;
        }
        key = context.Shortcuts.Add(Id, name, new ShortcutEdits(Saved, Change, () => Change(null)) { Icon = "Video" });
        _keys.Add(key); if (Saved() is { } initial) key.Register(initial, action);
    }
    public async Task StopAsync(CancellationToken ct)
    {
        _stopping = true;
        SystemEvents.SessionSwitch -= SessionSwitch;
        SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        if (Model is { } model)
        {
            await model.ShutdownAsync(); model.Changed -= Publish; model.ResultReady -= ResultReady;
            if (_context != null) model.Starting -= _context.CloseIsland;
            model.Dispose(); Model = null;
        }
        foreach (var key in _keys) key.Dispose(); _keys.Clear();
        _context?.Island.Clear(Id); _context?.Island.Clear(Id + "/saved"); _store?.Dispose(); _store = null; _context = null; Changed?.Invoke();
    }
}
