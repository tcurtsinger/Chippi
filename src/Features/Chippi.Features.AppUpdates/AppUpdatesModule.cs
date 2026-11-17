// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Windows.Threading;
using Chippi.Core.Actions;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Features.AppUpdates.Views;
using Chippi.UI.Actions;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.AppUpdates;

/// <summary>All update doors open the same reviewed confirmation; discovery never installs.</summary>
public sealed class AppUpdatesModule : IFeatureModule
{
    public const string Id = "app-updates";
    public const string Page = Id;
    public static readonly string[] ShortcutNames = ["Check app updates", "Update all apps"];
    public sealed record SettingsData
    {
        public Dictionary<string, KeyShortcut?> Shortcuts { get; init; } = [];
    }

    private readonly Func<IPackageSource> _createSource;
    private readonly Func<IReadOnlyList<ListedWindow>> _readWindows;
    private readonly Func<CancellationToken, Task> _waitForClose;
    private readonly List<IHotkey> _shortcuts = [];
    private ModuleContext? _context;
    private Dispatcher? _dispatcher;
    private IPackageSource? _source;
    private CancellationTokenSource? _lifetime;
    private AppUpdatesView? _view;
    private SettingsData _settings = new();
    private IReadOnlyList<ListedWindow> _windows = [];
    private Task? _running;
    private bool _wasRunning;
    private int _generation;
    private int _submittingGeneration = -1;

    public AppUpdatesModule() : this(() => new WindowsPackageSource()) { }
    public AppUpdatesModule(Func<IPackageSource> createSource, Func<IReadOnlyList<ListedWindow>>? readWindows = null,
        Func<CancellationToken, Task>? waitForClose = null)
    {
        _createSource = createSource;
        _readWindows = readWindows ?? (() => new WindowsWindowList().Read(includeOtherDesktops: true));
        _waitForClose = waitForClose ?? (token => Task.Delay(TimeSpan.FromMilliseconds(350), token));
    }

    public AppUpdatesEngine? Engine { get; private set; }
    public FeatureDescriptor Descriptor { get; } = new(Id, "App updates", "Finds and installs app updates through winget", FeatureGroup.Apps)
    {
        Icon = "RefreshCw", Requires = [Capability.Admin],
        Badges = [FeatureBadge.Needs("Admin for some apps")], Presets = PresetMembership.PowerUser,
    };

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context; _dispatcher = Dispatcher.CurrentDispatcher; _lifetime = new(); _generation++;
        _settings = context.Settings.Get<SettingsData>();
        _settings = _settings with { Shortcuts = _settings.Shortcuts ?? [] };
        _source = _createSource();
        Engine = new AppUpdatesEngine(_source, context.Admin, new JsonAppUpdatesStore(context.DataDirectory), Dispatch,
            onError: ex => context.Logger.LogWarning(ex, "Admin update batch failed"),
            onSourceError: error => context.Logger.LogWarning("Update source check failed: {Error}", error));
        Engine.Changed += Publish;
        context.Ui.SetSettingsPage(section => new AppUpdatesSettingsView(Engine, context, section));
        context.Ui.AddIslandTile(new IslandTileSpec(Page, "Update apps", "RefreshCw", 90, 0, View)
        {
            OnHome = false, HeaderHeight = 28, CreateHeader = () => View().Header,
            InitialFocus = () => View().InitialFocus,
            HandleBack = () => View().Back() || context.TryGoBackIslandPage("quick-actions"),
        });
        context.Ui.AddCommands(Commands);
        context.Ui.AddCommandSearch(Search);
        AddShortcut(ShortcutNames[0], () => _ = CheckAndOpenAsync());
        AddShortcut(ShortcutNames[1], () => _ = RequestAllAsync());
        return Task.CompletedTask;
    }

    private void Dispatch(Action action)
    {
        if (_dispatcher is not { } dispatcher || dispatcher.HasShutdownStarted) return;
        if (dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action);
    }

    private AppUpdatesView View() => _view ??= new AppUpdatesView(Engine!, _context!, ExecuteBatchAsync, IsRunning);

    public void Open()
    {
        if (_context is not { } context) return;
        _ = RefreshWindowsAsync(_generation);
        if (!context.TryOpenIslandPage(Page)) context.SummonIslandPage(Page);
    }

    internal Task CheckAndOpenAsync()
    {
        Open();
        return Engine is { } engine && _lifetime is { } lifetime ? engine.CheckAsync(lifetime.Token) : Task.CompletedTask;
    }

    private async Task RefreshWindowsAsync(int generation)
    {
        try
        {
            var windows = await Task.Run(_readWindows);
            if (_context != null && generation == _generation) _windows = windows;
        }
        catch (Exception ex) { _context?.Logger.LogDebug(ex, "App update window discovery failed"); }
    }

    internal bool IsRunning(PackageUpdate package) => _windows.Any(w => PackageWindowMatcher.Matches(package, w.Executable));

    private async Task RequestAllAsync()
    {
        var engine = Engine; var lifetime = _lifetime; int generation = _generation;
        if (engine == null || lifetime == null || engine.IsRunning) return;
        Open();
        if (engine.LastChecked == null) await engine.CheckAsync(lifetime.Token);
        if (_context != null && generation == _generation && !lifetime.IsCancellationRequested)
            View().ShowConfirmation(engine.CaptureBatch());
    }

    private Task RequestOneAsync(PackageUpdate package, bool close)
    {
        if (Engine is not { IsRunning: false } engine) return Task.CompletedTask;
        Open(); View().ShowConfirmation(engine.CaptureBatch([package]), close);
        return Task.CompletedTask;
    }

    internal async Task ExecuteBatchAsync(UpdateBatch batch, bool closeRunning)
    {
        if (Engine is not { IsRunning: false } engine || _context is not { } context || _lifetime is not { } lifetime)
            return;
        if (batch.Packages.Count == 0) return;
        int generation = _generation;
        if (_submittingGeneration == generation) return;
        _submittingGeneration = generation;
        try
        {
        if (closeRunning)
        {
            var pc = context.Actions ?? throw new ActionFailure("Close failed");
            var runner = new ActionRunner(pc, context.Commands, () => null, _ => "");
            var windows = await Task.Run(_readWindows);
            foreach (var window in windows.Where(w => batch.Packages.Any(p => PackageWindowMatcher.Matches(p, w.Executable))))
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (pc.CaptureWindow(window.Handle) is not { } captured ||
                    !batch.Packages.Any(p => PackageWindowMatcher.Matches(p, captured.Executable))) continue;
                await runner.RunAsync(new ChippiAction { Kind = ActionKind.Window, Value = nameof(WindowAction.Close) }, captured);
            }
            // A save dialog must never be forced away. Read again once after a bounded grace period.
            await _waitForClose(lifetime.Token);
            var remaining = await Task.Run(_readWindows);
            var blocked = batch.Packages.FirstOrDefault(p => remaining.Any(w => PackageWindowMatcher.Matches(p, w.Executable)));
            if (blocked != null) throw new ActionFailure($"Close {blocked.Name} and try again");
        }
        _running = engine.RunAsync(batch, lifetime.Token);
        await _running;
        }
        finally { if (_submittingGeneration == generation) _submittingGeneration = -1; }
    }

    private IReadOnlyList<ChippiCommand> Commands()
    {
        var commands = new List<ChippiCommand>
        {
            new(Id + "/check", "Update apps", "RefreshCw", CheckAndOpenAsync)
            { Words = "check app updates winget packages", Tile = new ActionsTile("apps", "Apps", "Package") { Menu = true } },
            new(Id + "/all", "Update all apps", "RefreshCw", RequestAllAsync) { Words = "update apps packages winget" },
        };
        if (Engine is { } engine)
            foreach (var package in engine.Updates)
            {
                var captured = package;
                commands.Add(new(Id + "/update/" + package.Key, "Update " + package.Name, "RefreshCw", () => RequestOneAsync(captured, false))
                { Words = "update " + package.Name, Detail = package.InstalledVersion + " → " + package.AvailableVersion });
                commands.Add(new(Id + "/skip/" + package.Key, "Skip " + package.Name + " update", "SkipForward",
                    () => { if (!engine.IsRunning) engine.Skip(captured, false); return Task.CompletedTask; })
                { Words = "skip app update " + package.Name });
                if (IsRunning(package))
                    commands.Add(new(Id + "/close-update/" + package.Key, "Close and update " + package.Name, "RefreshCw", () => RequestOneAsync(captured, true))
                    { Words = "close update " + package.Name });
            }
        return commands;
    }

    private IReadOnlyList<ChippiCommand> Search(string text)
    {
        if (!text.TrimStart().StartsWith("update ", StringComparison.OrdinalIgnoreCase) || Engine == null) return [];
        string query = text.Trim()[7..].Trim();
        if (query.Length == 0) return [];
        return Commands().Where(c => c.Key.StartsWith(Id + "/update/", StringComparison.Ordinal) &&
            c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void Publish()
    {
        if (_context is not { } context || Engine is not { } engine) return;
        if (engine.IsRunning)
            context.Island.Publish(new IslandActivity(Id, IslandActivityKind.Download, $"{engine.Completed} of {engine.Total} updated", "App updates")
            { OpensTile = Page, Icon = "RefreshCw", Progress = engine.Total == 0 ? null : engine.Completed / (double)engine.Total });
        else
        {
            context.Island.Clear(Id);
            if (_wasRunning)
                context.Island.Publish(new IslandActivity(Id + "/done", IslandActivityKind.Status, $"{engine.Completed} of {engine.Total} updated", "App updates")
                { OpensTile = Page, Icon = "RefreshCw", IsFeedback = true, NeedsAttention = engine.Rows.Any(r => r.State is UpdateState.Failed or UpdateState.NeedsAdmin) });
        }
        _wasRunning = engine.IsRunning;
    }

    private void AddShortcut(string name, Action action)
    {
        var context = _context!; IHotkey key = null!;
        string? Save(KeyShortcut? keys)
        {
            var old = _settings.Shortcuts.GetValueOrDefault(name);
            if (keys != null && !key.Register(keys, action)) return key.Error;
            var next = _settings with { Shortcuts = new(_settings.Shortcuts) { [name] = keys } };
            try { context.Settings.Save(next); _settings = next; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { if (old != null) key.Register(old, action); else key.Unregister(); return "Save failed"; }
            if (keys == null) key.Unregister(); return null;
        }
        key = context.Shortcuts.Add(Id, name, new ShortcutEdits(() => _settings.Shortcuts.GetValueOrDefault(name), Save, () => Save(null)) { Icon = "RefreshCw" });
        _shortcuts.Add(key);
        if (_settings.Shortcuts.GetValueOrDefault(name) is { } shortcut) key.Register(shortcut, action);
    }

    public Task StopAsync(CancellationToken ct)
    {
        _generation++; _lifetime?.Cancel();
        if (Engine != null) Engine.Changed -= Publish;
        _view?.Dispose(); _view = null;
        foreach (var key in _shortcuts) key.Dispose(); _shortcuts.Clear();
        _context?.Island.Clear(Id); _context?.Island.Clear(Id + "/done");
        var source = _source; var running = _running;
        if (source is IDisposable disposable)
        {
            if (running is { IsCompleted: false }) _ = running.ContinueWith(_ => disposable.Dispose(), TaskScheduler.Default);
            else disposable.Dispose();
        }
        _source = null; _context = null; Engine = null; _windows = []; _running = null;
        _lifetime?.Dispose(); _lifetime = null; _wasRunning = false;
        return Task.CompletedTask;
    }
}

internal static class PackageWindowMatcher
{
    /// <summary>Only an exact registered executable identity can offer Close; never a fuzzy name or shared folder.</summary>
    internal static bool Matches(PackageUpdate package, string executable)
    {
        if (string.IsNullOrWhiteSpace(package.IconPath) || !Path.IsPathFullyQualified(package.IconPath) ||
            !Path.IsPathFullyQualified(executable) || !package.IconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        try { return string.Equals(Path.GetFullPath(package.IconPath), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
