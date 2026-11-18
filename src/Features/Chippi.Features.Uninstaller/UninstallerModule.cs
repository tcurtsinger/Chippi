// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Text.RegularExpressions;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Features.Uninstaller.Views;

namespace Chippi.Features.Uninstaller;

/// <summary>
/// Uninstaller (docs/phase-7/uninstaller.md): Settings › Apps › Uninstaller is the list and the leftover review; the verbs are Search rows
/// ("uninstall notepad"), the Actions › Apps row and two shortcuts. Nothing is uninstalled or removed by itself: a verb row asks
/// <see cref="Request"/> (the island's question), and with the island off it only opens Settings with the app typed into the search.
/// </summary>
public sealed class UninstallerModule : IFeatureModule
{
    public const string Id = "uninstaller";
    public const string UninstallApp = "Uninstall app", ScanName = "Scan for leftovers";
    public static readonly string[] ShortcutNames = [UninstallApp, ScanName];
    private const string Icon = "Trash";
    /// <summary>The Settings section that opens the list with its search focused.</summary>
    public const string SearchSection = "search";
    /// <summary>The Settings section of the recently uninstalled list (Scan for leftovers).</summary>
    public const string RecentSection = "uninstalled";
    /// <summary>The Settings section that opens the leftover review of one recorded app.</summary>
    public const string ScanSection = "scan";
    /// <summary>The island page: the pickers, the question a verb row asks, its run and its result.</summary>
    public const string Page = Id;
    private static readonly Regex Verb = new(@"^uninstall\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly Func<UninstallerSystem> _createSystem;
    private readonly Func<LeftoverRoots> _roots;
    private readonly List<IHotkey> _shortcuts = [];
    private ModuleContext? _context;
    private UninstallerSettings _settings = new();
    private UninstallerView? _view;

    public UninstallerModule() : this(UninstallerSystem.ForWindows, LeftoverRoots.ForWindows) { }
    public UninstallerModule(Func<UninstallerSystem> createSystem, Func<LeftoverRoots> roots)
    {
        _createSystem = createSystem;
        _roots = roots;
    }

    public UninstallerEngine? Engine { get; private set; }

    /// <summary>
    /// The island's "Uninstall Notepad++?" question for one app, which runs <see cref="UninstallerEngine.UninstallAsync"/> when Uninstall is pressed.
    /// With the island off (or for a queue, which Settings asks) the row opens Settings at the app instead.
    /// </summary>
    public Func<IReadOnlyList<AppEntry>, Task>? Request { get; set; }

    /// <summary>The Scan for leftovers picker of recently uninstalled apps, on the island; with it off, Settings opens at the list.</summary>
    public Func<Task>? Pick { get; set; }

    /// <summary>"Stop uninstalling and quit?" on the island: true when Quit was pressed; null when no screen can ask, and the app asks itself.</summary>
    public Func<Task<bool?>>? QuitAsk { get; set; }

    public Task<bool?> AskQuitAsync() => QuitAsk is { } ask ? ask() : Task.FromResult<bool?>(null);

    public FeatureDescriptor Descriptor { get; } = new(Id, "Uninstaller", "Removes apps along with the leftovers they leave behind", FeatureGroup.Apps)
    {
        Icon = Icon, Requires = [Capability.Admin],
        Badges = [FeatureBadge.Needs("Admin for some apps")], Presets = PresetMembership.PowerUser,
    };

    /// <summary>The Settings section that opens the leftover review of the app with this record.</summary>
    public static string ScanSectionFor(Guid record) => ScanSection + ":" + record.ToString("N");

    /// <summary>The Settings section that opens the list searched for an app.</summary>
    public static string SectionFor(string text) => SearchSection + ":" + text;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        _settings = context.Settings.Get<UninstallerSettings>();
        _settings = _settings with { Shortcuts = _settings.Shortcuts ?? [] };
        string data = context.DataDirectory;
        Engine = new UninstallerEngine(_createSystem(), context.Admin, new RecordStore(Path.Combine(data, "records.json")), _roots(), Path.Combine(data, "backups"),
            Path.Combine(data, "apps.json"), context.Logger);
        Engine.Changed += UpdateBar;
        Engine.RunCompleted += PublishResult;
        _ = Engine.LoadCachedAsync();
        context.Ui.SetSettingsPage(section => new Views.UninstallerSettingsView(Engine!, context, section));
        context.Ui.AddIslandTile(new IslandTileSpec(Page, "Uninstall", Icon, 93, 0, () => View())
        {
            OnHome = false, HeaderHeight = 32, CreateHeader = () => View().Header,
            InitialFocus = () => View().InitialFocus, TakesKeyboard = true,
            HandleBack = () => View().Back(),
        });
        Request = apps =>
        {
            if (apps.Count == 1 && OpenPage()) View().Ask(apps[0]);
            else OpenSearch(apps.Count == 1 ? apps[0].Name : null);
            return Task.CompletedTask;
        };
        Pick = () => { if (OpenPage()) View().ShowScanPicker(); else _context?.OpenSettings(RecentSection); return Task.CompletedTask; };
        QuitAsk = async () => OpenPage() ? await View().AskQuitAsync() : null;
        context.Ui.AddCommands(Commands);
        context.Ui.AddCommandSearch(Search);
        AddShortcut(UninstallApp, () => PickApp());
        AddShortcut(ScanName, () => _ = ScanAsync());
        return Task.CompletedTask;
    }

    internal UninstallerView View() => _view ??= new UninstallerView(Engine!, _context!);

    /// <summary>Opens the island on the Uninstaller page; false while the island is off, when Settings opens instead.</summary>
    private bool OpenPage() => _context is { } context && Engine is not null && (context.TryOpenIslandPage(Page) || context.SummonIslandPage(Page));

    /// <summary>The Uninstall app shortcut: the island's picker of apps, or Settings with its search focused while the island is off.</summary>
    public void PickApp()
    {
        if (OpenPage()) { View().ShowPicker(); }
        else OpenSearch();
    }

    /// <summary>Settings opens on the list with its search focused.</summary>
    public void OpenSearch(string? text = null) => _context?.OpenSettings(text is { Length: > 0 } ? SectionFor(text) : SearchSection);

    /// <summary>Scan for leftovers: the picker of recently uninstalled apps, or Settings at that list.</summary>
    public Task ScanAsync()
    {
        if (Pick is { } pick) return pick();
        _context?.OpenSettings(RecentSection);
        return Task.CompletedTask;
    }

    private IReadOnlyList<ChippiCommand> Commands() =>
    [
        new(Id + "/page", "Uninstaller", Icon, () => { _context?.OpenSettings(null); return Task.CompletedTask; })
        { Kind = ChippiCommandKind.Page, Detail = "Settings", Words = "uninstaller apps programs remove" },
        new(Id + "/app", UninstallApp, Icon, () => { OpenSearch(); return Task.CompletedTask; }) { Kind = ChippiCommandKind.Page, Detail = "Settings", Words = "uninstall remove app program delete" },
        new(Id + "/scan", ScanName, Icon, ScanAsync) { Detail = "Uninstaller", Words = "leftovers uninstall remove scan registry folders" },
        // Actions › Apps: the row opens Settings with the search focused.
        new(Id + "/menu", "Uninstall", "ExternalLink", () => { OpenSearch(); return Task.CompletedTask; })
        { Detail = "Uninstaller", MenuOnly = true, Tile = new ActionsTile("apps", "Apps", "Package") { Menu = true } },
    ];

    /// <summary>"uninstall notepad" → one row per matching app (five at most), whose Run asks first; anything else is an ordinary search.</summary>
    internal IReadOnlyList<ChippiCommand> Search(string text)
    {
        if (Engine is not { } engine || Verb.Match(text.Trim()) is not { Success: true } match) return [];
        string query = match.Groups[1].Value.Trim();
        if (query.Length == 0) return [];
        if (!engine.Loaded && !engine.IsRefreshing) _ = engine.RefreshAsync();
        string key = UninstallerText.NameKey(query);
        var found = engine.Apps.Where(a => a.CanUninstall && (a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || key.Length > 0 && UninstallerText.NameKey(a.Name).Contains(key, StringComparison.Ordinal)))
            .OrderByDescending(a => a.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Take(5);
        return [.. found.Select(Row)];
    }

    /// <summary>
    /// The row of one app: "Uninstall Notepad++" over its size ("Store app" for a package), Run with the shield when the helper is used. An app that is
    /// open says "Running" and has no verb: Chippi never ends it.
    /// </summary>
    private ChippiCommand Row(AppEntry app)
    {
        bool open = Engine?.IsOpen(app) == true;
        string detail = open ? "Running" : app.IsStore ? "Store app" : UninstallerText.Size(app.SizeBytes);
        return new(Id + "/app/" + app.Id, "Uninstall " + app.Name, Icon, () => open || Request is not { } request ? Open(app) : request([app]))
        {
            Kind = ChippiCommandKind.Value, Detail = detail, Verb = open ? "" : "Run", NeedsAdmin = !open && app.NeedsAdmin, IconPath = app.IconPath,
        };
    }

    private Task Open(AppEntry app)
    {
        OpenSearch(app.Name);
        return Task.CompletedTask;
    }

    // ---- the bar ----

    /// <summary>
    /// The bar while it runs (sheet-bar): "Uninstalling" over the app, or over "2 of 3" in a queue; "Waiting for admin" while the Windows prompt is open;
    /// a window glyph while an uninstaller's own wizard is open. A click opens the page.
    /// </summary>
    private void UpdateBar()
    {
        if (_context is not { } context || Engine is not { } engine) return;
        if (!engine.IsRunning || engine.CurrentApp is not { } app) { context.Island.Clear(Id + "/run"); return; }
        string name = engine.Count > 1 ? engine.Index + " of " + engine.Count : app.Name;
        var activity = engine.WaitsForAdmin
            ? new IslandActivity(Id + "/run", IslandActivityKind.Status, "Waiting for admin", name) { Icon = "Shield" }
            : new IslandActivity(Id + "/run", IslandActivityKind.Status, "Uninstalling", name)
            { Progress = engine.Count > 1 ? (engine.Index - 1) / (double)engine.Count : null, Icon = app.IsQuiet ? "LoaderCircle" : "AppWindow" };
        context.Island.Publish(activity with { OpensTile = Page });
    }

    /// <summary>The bar after a queue: "Uninstalled", "Uninstall failed", "Needs admin" or "Restart to finish" over the app; it clears itself after the island's notice time.</summary>
    private void PublishResult(IReadOnlyList<AppOutcome> outcomes)
    {
        if (_context is not { } context) return;
        context.Island.Clear(Id + "/run");
        var bad = outcomes.FirstOrDefault(o => !o.Succeeded);
        string detail = outcomes.Count == 1 ? outcomes[0].App.Name : bad is not null ? bad.App.Name : outcomes.Count + " apps";
        string title, icon;
        switch (bad?.Status.State)
        {
            case null when outcomes.Any(o => o.Status.State == AppRunState.RestartNeeded): title = "Restart to finish"; icon = "RefreshCw"; break;
            case null: title = "Uninstalled"; icon = "CircleCheck"; break;
            case AppRunState.NeedsAdmin: title = "Needs admin"; icon = "Shield"; break;
            case AppRunState.Running: title = bad.App.Name + " is running"; detail = ""; icon = "AppWindow"; break;
            default: title = "Uninstall failed"; icon = "TriangleAlert"; break;
        }
        context.Island.Publish(new IslandActivity(Id + "/done", IslandActivityKind.Status, title, detail)
        { Icon = icon, IsFeedback = true, NeedsAttention = bad is not null, OpensTile = Page });
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
        if (Engine is { } engine)
        {
            engine.Changed -= UpdateBar;
            engine.RunCompleted -= PublishResult;
            _ = engine.EndAdminSessionAsync();   // a session a queue or a leftover step left open ends with the module
            engine.Dispose();
        }
        _context?.Island.Clear(Id + "/run");
        Engine = null;
        _context = null;
        return Task.CompletedTask;
    }
}
