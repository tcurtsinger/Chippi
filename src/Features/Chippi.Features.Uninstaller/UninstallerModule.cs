// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Text.RegularExpressions;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;

namespace Chippi.Features.Uninstaller;

/// <summary>
/// Uninstaller (docs/phase-7/uninstaller.md): Settings › Apps › Uninstaller is the list and the leftover review; the verbs are Search rows
/// ("uninstall notepad"), the Actions › Apps row and two shortcuts. Nothing is uninstalled or removed by itself: a verb row asks
/// <see cref="Request"/> (the screens' confirm), and without one it only opens Settings with the app typed into the search.
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
    private static readonly Regex Verb = new(@"^uninstall\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly Func<UninstallerSystem> _createSystem;
    private readonly Func<LeftoverRoots> _roots;
    private readonly List<IHotkey> _shortcuts = [];
    private ModuleContext? _context;
    private UninstallerSettings _settings = new();

    public UninstallerModule() : this(UninstallerSystem.ForWindows, LeftoverRoots.ForWindows) { }
    public UninstallerModule(Func<UninstallerSystem> createSystem, Func<LeftoverRoots> roots)
    {
        _createSystem = createSystem;
        _roots = roots;
    }

    public UninstallerEngine? Engine { get; private set; }

    /// <summary>The "Uninstall Notepad++?" confirm, then <see cref="UninstallerEngine.UninstallAsync"/>. Null while no screen is wired: the row opens Settings at the app.</summary>
    public Func<IReadOnlyList<AppEntry>, Task>? Request { get; set; }

    /// <summary>The Scan for leftovers picker of recently uninstalled apps. Null while no screen is wired: Settings opens at the list.</summary>
    public Func<Task>? Pick { get; set; }

    /// <summary>"Stop uninstalling and quit?" on the island or Settings: true when Quit was pressed; null when no screen can ask, and the app asks itself.</summary>
    public Func<Task<bool?>>? QuitAsk { get; set; }

    public Task<bool?> AskQuitAsync() => QuitAsk is { } ask ? ask() : Task.FromResult<bool?>(null);

    public FeatureDescriptor Descriptor { get; } = new(Id, "Uninstaller", "Removes apps along with the leftovers they leave behind", FeatureGroup.Apps)
    {
        Icon = Icon, Requires = [Capability.Admin],
        Badges = [FeatureBadge.Needs("Admin for some apps")], Presets = PresetMembership.PowerUser,
    };

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
        context.Ui.AddCommands(Commands);
        context.Ui.AddCommandSearch(Search);
        AddShortcut(UninstallApp, () => OpenSearch());
        AddShortcut(ScanName, () => _ = ScanAsync());
        return Task.CompletedTask;
    }

    /// <summary>Uninstall app: Settings opens on the list with its search focused.</summary>
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
        new(Id + "/app", UninstallApp, Icon, () => { OpenSearch(); return Task.CompletedTask; }) { Detail = "Uninstaller", Words = "uninstall remove app program delete" },
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

    private ChippiCommand Row(AppEntry app) => new(Id + "/app/" + app.Id, "Uninstall " + app.Name, Icon,
        () => Request is { } request ? request([app]) : Open(app))
    {
        Kind = ChippiCommandKind.Value, Detail = string.IsNullOrEmpty(app.Publisher) ? "Uninstaller" : app.Publisher, Verb = "Uninstall",
        NeedsAdmin = app.NeedsAdmin, IconPath = app.IconPath,
    };

    private Task Open(AppEntry app)
    {
        OpenSearch(app.Name);
        return Task.CompletedTask;
    }

    // ---- the bar ----

    private void UpdateBar()
    {
        if (_context is not { } context || Engine is not { } engine) return;
        if (engine.IsRunning && engine.CurrentName is { } name)
            context.Island.Publish(new IslandActivity(Id + "/run", IslandActivityKind.Status, "Uninstalling " + name, "Uninstaller")
            { Progress = engine.Count > 1 ? (engine.Index - 1) / (double)engine.Count : null, Icon = Icon });
        else context.Island.Clear(Id + "/run");
    }

    /// <summary>The bar after a queue: "Uninstalled" over the app, or the caution words; it clears itself after the island's notice time.</summary>
    private void PublishResult(IReadOnlyList<AppOutcome> outcomes)
    {
        if (_context is not { } context) return;
        context.Island.Clear(Id + "/run");
        var bad = outcomes.FirstOrDefault(o => !o.Succeeded);
        var title = bad is not null ? bad.Status.Text ?? "Uninstall failed" : outcomes.Any(o => o.Status.State == AppRunState.RestartNeeded) ? "Restart to finish" : "Uninstalled";
        string detail = outcomes.Count == 1 ? outcomes[0].App.Name : bad is not null ? bad.App.Name : outcomes.Count + " apps";
        string icon = bad is null ? Icon : bad.Status.State == AppRunState.NeedsAdmin ? "Shield" : "TriangleAlert";
        context.Island.Publish(new IslandActivity(Id + "/done", IslandActivityKind.Status, title, detail)
        { Icon = icon, IsFeedback = true, NeedsAttention = bad is not null });
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
