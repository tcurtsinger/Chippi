// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Modules;
using Chippi.Features.PortManager.Views;
using Chippi.Core.Shortcuts;

namespace Chippi.Features.PortManager;

/// <summary>
/// Port manager (docs/phase-7/port-manager.md): "port 3000" in Search names the holder and offers its verb; Settings › System › Ports is the
/// live list. Nothing here ends anything by itself: a verb row asks <see cref="Request"/> (the screens' question), and without
/// one it only opens the Ports page.
/// </summary>
public sealed class PortManagerModule : IFeatureModule
{
    public const string Id = "port-manager";
    public const string FindPort = "Find port", FreePort = "Free port";
    public static readonly string[] ShortcutNames = [FindPort, FreePort];
    /// <summary>The Command bar's island page: a shortcut summons it, until the bar takes a starting text.</summary>
    internal const string SearchPage = "command-bar";
    private const string Icon = "Network";
    /// <summary>The island page: the Free port picker and the question a verb row asks.</summary>
    public const string Page = Id;

    public sealed record SettingsData
    {
        public Dictionary<string, KeyShortcut?> Shortcuts { get; init; } = [];
    }

    private readonly Func<IPortSystem> _createSystem;
    private readonly List<IHotkey> _shortcuts = [];
    private ModuleContext? _context;
    private SettingsData _settings = new();
    private PortManagerView? _view;

    public PortManagerModule() : this(() => new WindowsPortSystem()) { }
    public PortManagerModule(Func<IPortSystem> createSystem) => _createSystem = createSystem;

    public PortManagerEngine? Engine { get; private set; }

    /// <summary>
    /// The screens' confirm for a row's verb (Free or Stop): asks "End node.exe?", then calls <see cref="PortManagerEngine.FreeAsync"/> or
    /// <see cref="PortManagerEngine.StopAsync"/>. Null while no screen is wired: the row then opens Settings › Ports at the port.
    /// </summary>
    public Func<PortRow, Task>? Request { get; set; }

    /// <summary>The Free port shortcut's picker of listening ports (an island page); null while none is wired.</summary>
    public Func<Task>? Pick { get; set; }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Ports", "Shows which app holds a network port and frees it", FeatureGroup.System)
    {
        Icon = Icon, Requires = [Capability.Admin],
        Badges = [FeatureBadge.Needs("Admin for some ports")], Presets = PresetMembership.PowerUser,
    };

    /// <summary>The Settings section that opens Ports filtered to one port (a Search row's Open).</summary>
    public static string SectionFor(int port) => "port:" + port;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        _settings = context.Settings.Get<SettingsData>();
        _settings = _settings with { Shortcuts = _settings.Shortcuts ?? [] };
        Engine = new PortManagerEngine(_createSystem(), context.Admin);
        context.Ui.SetSettingsPage(section => new PortsSettingsView(Engine!, context, section));
        context.Ui.AddIslandTile(new IslandTileSpec(Page, "Free port", Icon, 92, 0, () => View())
        {
            OnHome = false, HeaderHeight = 28, CreateHeader = () => View().Header,
            InitialFocus = () => View().InitialFocus,
            HandleBack = () => View().Back(),
        });
        Request = row => { if (OpenPage()) View().Ask(row); else OpenPorts(row.Endpoint.LocalPort); return Task.CompletedTask; };
        Pick = () => { if (OpenPage()) View().ShowPicker(); else OpenPorts(); return Task.CompletedTask; };
        context.Ui.AddCommands(Commands);
        context.Ui.AddCommandSearch(Search);
        AddShortcut(FindPort, FindPortSearch);
        AddShortcut(FreePort, () =>
        {
            if (Pick is { } pick) _ = pick();
            else context.SummonIslandPage(SearchPage);
        });
        return Task.CompletedTask;
    }

    /// <summary>Search with "port " typed; plain Search when the Command bar can't take text.</summary>
    private void FindPortSearch()
    {
        if (_context is { } context && !context.SummonSearch("port "))
            context.SummonIslandPage(SearchPage);
    }

    private PortManagerView View() => _view ??= new PortManagerView(Engine!, _context!);

    /// <summary>Opens the island on Port manager's page; false while the island is off, when Settings opens instead.</summary>
    private bool OpenPage() => _context is { } context && (context.TryOpenIslandPage(Page) || context.SummonIslandPage(Page));

    internal void OpenPorts(int? port = null) => _context?.OpenSettings(port is { } p ? SectionFor(p) : null);

    private ChippiCommand PortsCommand(int? port = null) =>
        new(Id + "/ports", "Ports", Icon, () => { OpenPorts(port); return Task.CompletedTask; })
        { Kind = ChippiCommandKind.Page, Detail = "Settings › System", Words = "ports network listening" };

    private IReadOnlyList<ChippiCommand> Commands() =>
    [
        PortsCommand(),
        new(Id + "/find", FindPort, Icon, () => { FindPortSearch(); return Task.CompletedTask; }) { Words = "port network listening who" },
        new(Id + "/free", FreePort, Icon, () =>
        {
            if (Pick is { } pick) return pick();
            _context?.SummonIslandPage(SearchPage);
            return Task.CompletedTask;
        }) { Words = "port network end stop kill" },
    ];

    /// <summary>"port 3000" → the holder's row and the Ports row; "port 70000" → Invalid port; anything else is an ordinary search.</summary>
    internal IReadOnlyList<ChippiCommand> Search(string text)
    {
        if (Engine is not { } engine) return [];
        switch (PortQuery.Parse(text))
        {
            case PortQuery.Port(var port):
                return [Row(port, PortAnswer.For(port, engine.Lookup(port))), PortsCommand(port)];
            case PortQuery.Invalid:
                return [new ChippiCommand(Id + "/invalid", "Invalid port", Icon, () => { OpenPorts(); return Task.CompletedTask; })
                    { Kind = ChippiCommandKind.Value, Detail = "", Verb = "" }, PortsCommand()];
            default:
                return [];
        }
    }

    private ChippiCommand Row(int port, PortAnswer answer)
    {
        var row = answer.Row;
        Func<Task> run = answer.Verb switch
        {
            "Free" or "Stop" when row is not null => () => Request is { } request ? request(row) : OpenAt(port),
            _ => () => OpenAt(port),
        };
        return new ChippiCommand(Id + "/port", answer.Name, Icon, run)
        {
            Kind = ChippiCommandKind.Value, Detail = answer.Detail ?? "", Verb = answer.Verb ?? "", NeedsAdmin = answer.NeedsAdmin, IconPath = answer.IconPath,
        };
    }

    private Task OpenAt(int port)
    {
        OpenPorts(port);
        return Task.CompletedTask;
    }

    private void AddShortcut(string name, Action action)
    {
        var context = _context!;
        IHotkey key = null!;
        string? Save(KeyShortcut? keys)
        {
            var old = _settings.Shortcuts.GetValueOrDefault(name);
            if (keys != null && !key.Register(keys, action)) return key.Error;
            var next = _settings with { Shortcuts = new(_settings.Shortcuts) { [name] = keys } };
            try { context.Settings.Save(next); _settings = next; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                if (old != null) key.Register(old, action); else key.Unregister();
                return "Save failed";
            }
            if (keys == null) key.Unregister();
            return null;
        }
        key = context.Shortcuts.Add(Id, name, new ShortcutEdits(() => _settings.Shortcuts.GetValueOrDefault(name), Save, () => Save(null)) { Icon = Icon });
        _shortcuts.Add(key);
        if (_settings.Shortcuts.GetValueOrDefault(name) is { } shortcut) key.Register(shortcut, action);
    }

    public Task StopAsync(CancellationToken ct)
    {
        foreach (var key in _shortcuts) key.Dispose();
        _shortcuts.Clear();
        _view?.Dispose();
        _view = null;
        if (Engine is { } engine) _ = engine.EndAdminSessionAsync();   // a session a verb left open ends with the module
        Engine?.Dispose();
        Engine = null;
        _context = null;
        return Task.CompletedTask;
    }
}
