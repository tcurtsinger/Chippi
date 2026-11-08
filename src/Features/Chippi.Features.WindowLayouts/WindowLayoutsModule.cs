// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Modules;
using Chippi.Core.Island;
using Microsoft.Win32;
using System.Windows.Threading;

namespace Chippi.Features.WindowLayouts;

/// <summary>Snap's settings and commands. The shell supplies the PC; views use the same captured target and runner.</summary>
public sealed partial class WindowLayoutsModule(Func<IReadOnlyList<SnapDisplay>>? displays = null) : IFeatureModule
{
    public const string Id = "window-layouts";
    private ModuleContext? _context;
    private ActionRunner? _runner;
    private WindowsSnapDrag? _nativeDrag;
    private IZoneOverlay? _overlay;
    private Dispatcher? _ui;
    public Func<IZoneOverlay>? CreateOverlay { get; set; }
    public IReadOnlyList<SnapDisplay> Displays => displays?.Invoke() ?? [];
    public event Action? DisplaysChanged;
    public event Action<string>? Failed;
    public SnapLayoutStore? Layouts { get; private set; }
    public Action<WindowLayoutsModule, ModuleContext>? ComposeViews { get; set; }
    public event Action? Changed;

    public FeatureDescriptor Descriptor { get; } = new(Id, "Snap",
        "Place windows in halves, thirds and custom zones", FeatureGroup.Windows) { Icon = "LayoutPanelLeft", PausesInFullscreen = true };

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        _context = context;
        _ui = Dispatcher.CurrentDispatcher;
        Layouts = new SnapLayoutStore(context.Settings);
        Layouts.Changed += LayoutsChanged;
        if (context.Actions is { } pc)
            _runner = new ActionRunner(pc, context.Commands, () => context.Clipboard, text => text,
                () => context.IslandPasteTarget()?.Window ?? 0);
        StartArrangements(context);
        context.Ui.AddCommands(Commands);
        context.Ui.SetSettingsPage(_ => new Views.SnapPage(this));
        ComposeViews?.Invoke(this, context);
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        UpdateDrag();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        StopDrag();
        StopArrangements();
        if (Layouts is { } layouts) layouts.Changed -= LayoutsChanged;
        _context?.Island.Clear("snap-failed");
        Layouts = null;
        _runner = null;
        _context = null;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    private void LayoutsChanged()
    {
        SyncArrangementShortcuts();
        UpdateDrag();
        Changed?.Invoke();
    }

    private void DisplaySettingsChanged(object? sender, EventArgs args) => _ui?.BeginInvoke(() =>
    {
        Changed?.Invoke(); // Cancels an active native drag before publishing changed display geometry.
        DisplaysChanged?.Invoke();
    });

    private void UpdateDrag()
    {
        if (Layouts?.Settings.ShiftDragEnabled != true) { StopDrag(); return; }
        if (_nativeDrag is not null || CreateOverlay is null || _context?.Actions is not { } pc) return;
        try
        {
            _overlay = CreateOverlay();
            var controller = new SnapDragController(this, () => Displays, _overlay);
            _nativeDrag = new WindowsSnapDrag(this, _context.Input, pc.CaptureWindow, controller);
            _nativeDrag.Failed += ReportFailure;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StopDrag();
            ReportFailure("Snap");
        }
    }

    /// <summary>Uses the shell's transient action-failure bar, including actions fired through other doors.</summary>
    public void ReportFailure(string app)
    {
        if (_ui is null) return;
        if (!_ui.CheckAccess()) { _ui.BeginInvoke(() => ReportFailure(app)); return; }
        if (_context is not { } context) return;
        Failed?.Invoke("Snap failed");
        if (!context.CanShowIslandFeedback()) return;
        context.Island.Publish(new IslandActivity("snap-failed", IslandActivityKind.Status, "Snap failed", app)
        { IsFeedback = true, NeedsAttention = true, Icon = "LayoutGrid" });
    }

    private void StopDrag()
    {
        _nativeDrag?.Dispose();
        _nativeDrag = null;
        _overlay?.Dispose();
        _overlay = null;
    }

    public ActionWindow? CaptureTarget()
    {
        if (_context?.Actions is not { } pc) return null;
        if (_context.IslandPasteTarget() is { } prior)
        {
            var captured = pc.CaptureWindow(prior.Window);
            return captured?.ProcessId == prior.ProcessId ? captured : null;
        }
        return pc.CaptureWindow(pc.WindowInFront());
    }

    public bool CanMove(ActionWindow target) => _context?.Actions?.CanSnap(target) == true;
    public void BeginDrag(ActionWindow target) => _context?.Actions?.BeginWindowDrag(target);
    public void EndDrag(ActionWindow target) => _context?.Actions?.EndWindowDrag(target);

    public bool CanRestore(ActionWindow target) => _context?.Actions?.CanRestore(target) == true;

    public WindowZone? ResolveZone(string layoutId, string zoneId)
    {
        if (Layouts is not { } store) return null;
        var layout = store.Settings.Layouts.FirstOrDefault(l => l.Id == layoutId);
        var zone = layout?.Root.Flatten().Zones.FirstOrDefault(z => z.Id == zoneId);
        return zone is null ? null : new(zone.X, zone.Y, zone.Width, zone.Height,
            store.Settings.WindowGap, store.Settings.ScreenEdgeGap);
    }

    public Task RunAsync(ActionWindow target, WindowAction verb) =>
        _runner?.RunAsync(new ChippiAction { Kind = ActionKind.Window, Value = verb.ToString() }, target)
            ?? throw new ActionFailure("Snap failed");

    public Task RunAsync(ActionWindow target, SnapZoneKey zone, SnapDisplay? display = null) =>
        _runner?.RunAsync(new ChippiAction { Kind = ActionKind.Window, Value = nameof(WindowAction.Zone),
            LayoutId = zone.LayoutId, ZoneId = zone.ZoneId }, target, display?.Handle ?? 0) ?? throw new ActionFailure("Snap failed");

    /// <summary>The Actions tile: its menu is Snap's own view (pictures and commands), not a row per command.</summary>
    private ActionsTile SnapTile => _snapTile ??= new("snap", "Snap", "LayoutGrid") { Menu = true, View = () => new Views.SnapMenu(this), OpensNow = TakeRunAsk };
    private ActionsTile? _snapTile;

    private IReadOnlyList<ChippiCommand> Commands()
    {
        if (Layouts is not { } store) return [];
        var target = CaptureTarget();
        var commands = new List<ChippiCommand>();
        foreach (var layout in store.Settings.Layouts)
        foreach (var zone in layout.Root.Flatten().Zones)
        {
            var key = new SnapZoneKey(layout.Id, zone.Id);
            commands.Add(new($"{Id}/zone/{layout.Id}/{zone.Id}", $"{layout.Name}: {zone.Name}", "LayoutGrid",
                () => RunAsync(target ?? throw new ActionFailure("Snap failed"), key))
            { Words = "snap window layout", Detail = target?.App ?? "Snap", Tile = SnapTile,
                WindowAction = new() { Kind = ActionKind.Window, Value = nameof(WindowAction.Zone), LayoutId = key.LayoutId, ZoneId = key.ZoneId } });
        }
        foreach (var (verb, name, icon) in new[]
        {
            (WindowAction.Maximize, "Maximize", "Maximize"), (WindowAction.Center, "Center", "AppWindow"),
            (WindowAction.OtherScreen, "Next monitor", "OtherScreen"), (WindowAction.Restore, "Restore", "AppWindow"),
        })
        {
            commands.Add(new($"{Id}/{verb}", name, icon, () => RunAsync(target ?? throw new ActionFailure("Snap failed"), verb))
            { Words = "snap window", Detail = target?.App ?? "Snap", Tile = SnapTile,
                WindowAction = new() { Kind = ActionKind.Window, Value = verb.ToString() } });
        }
        AddArrangementCommands(commands);
        return commands;
    }
}


