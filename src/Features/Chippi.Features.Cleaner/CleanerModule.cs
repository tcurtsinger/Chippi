// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Cleaner.Views;
using Chippi.Core.Shortcuts;

namespace Chippi.Features.Cleaner;

/// <summary>
/// Cleaner (docs/phase-7/cleaner.md): temp files and caches, measured first and cleared in one step. Nothing is deleted here without
/// <see cref="Confirm"/>: a verb measures, then asks the screens' question; without a screen wired it only opens Settings.
/// </summary>
public sealed class CleanerModule : IFeatureModule
{
    public const string Id = "cleaner";
    public const string ScanName = "Scan for junk files", CleanName = "Clean junk files";
    public static readonly string[] ShortcutNames = [ScanName, CleanName];
    private const string Icon = "Trash2";
    /// <summary>The island page: the list, the confirm and the result.</summary>
    public const string Page = Id;

    private readonly CleanerRoots _roots;
    private readonly Func<IReadOnlySet<string>>? _running;
    private readonly List<IHotkey> _shortcuts = [];
    private ModuleContext? _context;
    private CleanerSettings _settings = new();
    private CancellationTokenSource? _lifetime;
    private CleanerView? _view;
    private bool _listOpened;
    private Func<CleanerPlan, Task>? _wired;

    public CleanerModule() : this(CleanerRoots.ForWindows()) { }
    public CleanerModule(CleanerRoots roots, Func<IReadOnlySet<string>>? runningProcesses = null)
    {
        _roots = roots;
        _running = runningProcesses;
    }

    public CleanerEngine? Engine { get; private set; }

    /// <summary>
    /// The screens' confirm: asks the plan's question ("Clean 1.0 GB?", Cancel and Clean), then calls <see cref="CleanerEngine.CleanAsync"/> or
    /// <see cref="CleanerEngine.CancelPlanAsync"/>. Null while no screen is wired.
    /// </summary>
    public Func<CleanerPlan, Task>? Confirm { get; set; }

    /// <summary>Opens the list on the island (true), measuring when it opens. Null while no screen is wired: Settings opens instead.</summary>
    public Func<bool>? OpenList { get; set; }

    /// <summary>The screens' "Nothing to clean" state, shown when a verb found nothing to delete. Null while no screen is wired.</summary>
    public Action? Empty { get; set; }

    /// <summary>"Stop cleaning and quit?" on the island: true when Quit was pressed; null when the island can't ask, and the app asks itself.</summary>
    public async Task<bool?> AskQuitAsync()
    {
        if (Engine is null || OpenList is not { } open) return null;
        _listOpened = open();
        return _listOpened ? await View().AskQuitAsync() : null;
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Cleaner", "Clears temp files, caches and other space hogs", FeatureGroup.Apps)
    {
        Icon = Icon, Requires = [Capability.Admin],
        Badges = [FeatureBadge.Needs("Admin for some categories")],
    };

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        _lifetime = new();
        _settings = context.Settings.Get<CleanerSettings>();
        _settings = _settings with { Ticks = _settings.Ticks ?? [], Sizes = _settings.Sizes ?? [], Shortcuts = _settings.Shortcuts ?? [] };
        Engine = new CleanerEngine(_roots, context.Admin, _settings, Persist, _running);
        Engine.RunCompleted += PublishResult;
        context.Ui.SetSettingsPage(_ => new CleanerSettingsView(Engine!, context));
        context.Ui.AddIslandTile(new IslandTileSpec(Page, "Clean junk files", Icon, 91, 0, () => View())
        {
            OnHome = false, HeaderHeight = 28, CreateHeader = () => View().Header,
            InitialFocus = () => View().InitialFocus,
            HandleBack = () => View().Back() || context.TryGoBackIslandPage("quick-actions"),
        });
        OpenList = () => context.TryOpenIslandPage(Page) || context.SummonIslandPage(Page);
        Confirm = async plan =>
        {
            if (!_listOpened) { await Engine!.CancelPlanAsync(); return; }
            await View().AskAsync(plan);
        };
        _wired = Confirm;
        Empty = () => { if (_listOpened) View().ShowEmpty(); };
        context.Ui.AddCommands(Commands);
        AddShortcut(ScanName, () => _ = ScanAsync());
        AddShortcut(CleanName, () => _ = RequestCleanAsync());
        return Task.CompletedTask;
    }

    /// <summary>Ticks and sizes save through here; the shortcuts section is the module's own and is kept.</summary>
    private void Persist(CleanerSettings next)
    {
        var merged = next with { Shortcuts = _settings.Shortcuts };
        _context!.Settings.Save(merged);
        _settings = merged;
    }

    /// <summary>Scan for junk files: opens the list and measures.</summary>
    public async Task ScanAsync()
    {
        if (Engine is not { } engine || _lifetime is not { } lifetime) return;
        if (Open()) View().ShowList();
        try { await engine.MeasureAsync(lifetime.Token); }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Clean junk files: opens the list, measures, and asks. Never cleans silently: with no confirm wired, or with nothing to clean, it deletes nothing.
    /// With an admin category ticked, the helper measures first (the UAC prompt) and the question shows the full size.
    /// </summary>
    public async Task RequestCleanAsync(IReadOnlyList<string>? only = null)
    {
        if (Engine is not { } engine || _lifetime is not { } lifetime) return;
        if (!Open() && ReferenceEquals(Confirm, _wired)) return;   // the island is off: no measure and no prompt for a question nothing can show
        CleanerPlan? plan;
        try { plan = await engine.PrepareCleanAsync(only, lifetime.Token); }
        catch (OperationCanceledException) { return; }
        if (plan is null) return;
        if (plan.IsEmpty || Confirm is not { } confirm)
        {
            await engine.CancelPlanAsync();   // the question never gets asked: the session the measure left open ends
            if (plan.IsEmpty) Empty?.Invoke();
            return;
        }
        await confirm(plan);
    }

    /// <summary>Opens the list on the island; false when it can't (the island is off), when Settings opens instead.</summary>
    private bool Open()
    {
        _listOpened = OpenList is { } open && open();
        if (!_listOpened) _context?.OpenSettings(null);
        return _listOpened;
    }

    internal CleanerView View() => _view ??= new CleanerView(Engine!, only => RequestCleanAsync(only), () => _lifetime?.Token ?? CancellationToken.None);

    /// <summary>The bar after a run: "1.0 GB freed" over Cleaner, or the caution lines; it clears itself after the island's notice time.</summary>
    private void PublishResult(CleanerRunResult result)
    {
        if (_context is not { } context) return;
        string icon = result.Failed > 0 || result.HelperFailed ? "TriangleAlert" : result.NeedsAdmin > 0 ? "Shield" : Icon;
        context.Island.Publish(new IslandActivity(Id + "/done", IslandActivityKind.Status, result.BarTitle, result.BarDetail)
        { OpensTile = Page, Icon = icon, IsFeedback = true, NeedsAttention = result.BarCaution });
    }

    private IReadOnlyList<ChippiCommand> Commands()
    {
        string total = Engine is { HasSizes: true } engine ? engine.TotalText : "";
        return
        [
            new(Id + "/page", "Cleaner", Icon, () => { _context?.OpenSettings(null); return Task.CompletedTask; })
            { Kind = ChippiCommandKind.Page, Detail = "Settings", Words = "cleaner junk temp cache" },
            new(Id + "/scan", ScanName, Icon, ScanAsync) { Detail = "Cleaner", Words = "junk temp cache cleaner measure" },
            new(Id + "/clean", CleanName, Icon, () => RequestCleanAsync())
            { Words = "junk temp cache cleaner delete", NeedsAdmin = Engine?.AnyAdminTicked == true, Detail = "Cleaner" },
            // Actions › Apps: the row opens the list, measured on open; Clean there asks.
            new(Id + "/list", CleanName, Icon, ScanAsync)
            { Detail = "Cleaner", MenuNote = total, MenuOnly = true, Tile = new ActionsTile("apps", "Apps", "Package") { Menu = true } },
        ];
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
        _lifetime?.Cancel();
        if (Engine is { } engine)
        {
            _ = engine.CancelPlanAsync();   // a session a measure left open ends with the module
            engine.Dispose();
        }
        Engine = null;
        _lifetime?.Dispose();
        _lifetime = null;
        _context = null;
        return Task.CompletedTask;
    }
}
