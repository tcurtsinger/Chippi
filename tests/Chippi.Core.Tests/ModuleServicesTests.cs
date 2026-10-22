// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.Json.Nodes;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Notifications;
using Chippi.Core.SystemIntegration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Core.Tests;

/// <summary>Phase 2 contracts: module data folders, notices, activation verbs and settings sections.</summary>
public sealed class ModuleServicesTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ConfigStore _config;
    private readonly IslandActivityHub _island = new();
    private readonly ModuleServices _services;
    private readonly ModuleRegistry _registry;

    public ModuleServicesTests()
    {
        _config = new ConfigStore(_dir.File("settings.json"));
        _services = new ModuleServices { DataRoot = Path.Combine(_dir.Path, "data"), IslandNoticeDuration = TimeSpan.FromMilliseconds(150) };
        _registry = new ModuleRegistry(_config, _island, NullLoggerFactory.Instance, _services);
    }

    public void Dispose() => _dir.Dispose();

    private static readonly NoticeKind[] Kinds =
    [
        new("organized", "Organized", NoticePlaces.Island),
        new("failed", "Failed", NoticePlaces.Windows | NoticePlaces.Island),
    ];

    private async Task<FakeModule> StartAsync(string id, Action<ModuleContext>? onStart = null)
    {
        var module = FakeModule.Create(id);
        module.OnStart = onStart;
        _registry.Register(module);
        await _registry.StartAsync();
        await _registry.SetInstalledAsync(id, true);
        return module;
    }

    private sealed class RecordingToasts : IToastPresenter
    {
        public List<(string Module, Notice Notice)> Shown { get; } = [];

        public bool Throw { get; set; }

        public void Show(string moduleId, Notice notice)
        {
            if (Throw)
                throw new InvalidOperationException("backend broke");
            Shown.Add((moduleId, notice));
        }
    }

    // ---- Data folder

    [Fact]
    public async Task Each_module_gets_its_own_data_folder_that_outlives_uninstall()
    {
        var module = await StartAsync("downloads");
        string folder = module.Context!.DataDirectory;

        Assert.Equal(Path.Combine(_dir.Path, "data", "downloads"), folder);
        Assert.True(Directory.Exists(folder));
        File.WriteAllText(Path.Combine(folder, "history.db"), "x");

        await _registry.SetInstalledAsync("downloads", false);
        Assert.True(File.Exists(Path.Combine(folder, "history.db")));
    }

    // ---- Notices

    [Fact]
    public async Task Places_default_per_kind_and_saved_places_win()
    {
        var module = await StartAsync("downloads", ctx => ctx.Notifier.Declare(Kinds));
        var notifier = module.Context!.Notifier;

        Assert.Equal(NoticePlaces.Island, notifier.PlacesFor("organized"));
        Assert.Equal(NoticePlaces.None, notifier.PlacesFor("unknown"));

        notifier.SetPlaces("organized", NoticePlaces.Windows | NoticePlaces.Island);
        Assert.Equal(NoticePlaces.Windows | NoticePlaces.Island, notifier.PlacesFor("organized"));
    }

    [Fact]
    public async Task Places_live_in_the_module_section_beside_its_other_settings()
    {
        var module = await StartAsync("downloads", ctx => ctx.Notifier.Declare(Kinds));
        module.Context!.Settings.Save(new FakeSettings { Value = 7 });
        module.Context.Notifier.SetPlaces("failed", NoticePlaces.None);
        module.Context.Settings.Save(new FakeSettings { Value = 8 }); // a later save keeps the places

        var section = JsonNode.Parse(File.ReadAllText(_config.FilePath))!["modules"]!["downloads"]!;
        Assert.Equal(8, section["value"]!.GetValue<int>());
        Assert.Equal("none", section["notices"]!["failed"]!.GetValue<string>());
        Assert.Equal(NoticePlaces.None, module.Context.Notifier.PlacesFor("failed"));
    }

    [Fact]
    public async Task A_notice_reaches_exactly_its_places()
    {
        var toasts = new RecordingToasts();
        _services.Toasts = toasts;
        var module = await StartAsync("downloads", ctx => ctx.Notifier.Declare(Kinds));
        var notifier = module.Context!.Notifier;

        notifier.SetPlaces("organized", NoticePlaces.Windows);
        notifier.Show(new Notice("organized", "4 files organized"));
        Assert.Single(toasts.Shown);
        Assert.Null(_island.Current);

        notifier.SetPlaces("organized", NoticePlaces.Island);
        notifier.Show(new Notice("organized", "2 files organized") { Body = "Downloads" });
        Assert.Single(toasts.Shown);
        Assert.Equal("2 files organized", _island.Current!.Title);
        Assert.Equal("Downloads", _island.Current.Source);
        Assert.Equal(IslandActivityKind.Stash, _island.Current.Kind);

        notifier.SetPlaces("organized", NoticePlaces.None);
        notifier.Show(new Notice("organized", "1 file organized"));
        Assert.Single(toasts.Shown);
        Assert.Single(_island.Activities);
    }

    [Fact]
    public async Task The_app_switches_silence_a_place_and_keep_each_choice()
    {
        var toasts = new RecordingToasts();
        _services.Toasts = toasts;
        var module = await StartAsync("downloads", ctx => ctx.Notifier.Declare(Kinds));
        var notifier = module.Context!.Notifier;
        notifier.SetPlaces("organized", NoticePlaces.Windows | NoticePlaces.Island);

        _config.SetSection(AppSettings.SectionName, new AppSettings { WindowsNotices = false });
        notifier.Show(new Notice("organized", "1 file organized"));
        Assert.Empty(toasts.Shown);
        Assert.Single(_island.Activities);

        _config.SetSection(AppSettings.SectionName, new AppSettings { IslandNotices = false });
        notifier.Show(new Notice("organized", "2 files organized"));
        Assert.Single(toasts.Shown);
        Assert.Single(_island.Activities);

        Assert.Equal(NoticePlaces.Windows | NoticePlaces.Island, notifier.PlacesFor("organized")); // the choice is kept
    }

    [Fact]
    public async Task An_island_notice_clears_itself_and_can_celebrate()
    {
        ModuleSucceededEventArgs? celebrated = null;
        _island.Succeeded += (_, e) => celebrated = e;
        var module = await StartAsync("downloads", ctx => ctx.Notifier.Declare(Kinds));

        module.Context!.Notifier.Show(new Notice("organized", "4 files organized") { Celebrate = true });

        Assert.NotNull(_island.Current);
        Assert.Equal("downloads", celebrated!.ModuleId);
        for (int i = 0; i < 40 && _island.Current is not null; i++)
            await Task.Delay(50);
        Assert.Null(_island.Current);
    }

    [Fact]
    public async Task A_missing_or_broken_Windows_backend_never_fails_the_caller()
    {
        var module = await StartAsync("downloads", ctx => ctx.Notifier.Declare(Kinds));
        var notice = new Notice("failed", "1 file failed");

        module.Context!.Notifier.Show(notice); // no backend yet
        _services.Toasts = new RecordingToasts { Throw = true };
        module.Context.Notifier.Show(notice);

        Assert.Equal(2, _island.Activities.Count); // the island part still happened, twice
    }

    [Fact]
    public void Duplicate_kind_ids_are_rejected()
    {
        var notifier = new ModuleNotifier("m", _config, _island.CreatePublisher("m"), _services, NullLogger.Instance);
        Assert.Throws<ArgumentException>(() => notifier.Declare([new("a", "A", NoticePlaces.None), new("A", "A again", NoticePlaces.None)]));
    }

    [Fact]
    public async Task The_app_wide_page_reads_and_sets_places_through_the_feature_entry()
    {
        Assert.Null(_registry.Find("downloads")); // not registered yet
        var module = await StartAsync("downloads", ctx => ctx.Notifier.Declare(Kinds));
        var entry = _registry.Find("downloads")!;

        Assert.Equal(["organized", "failed"], entry.Notifier!.Kinds.Select(k => k.Id));
        entry.Notifier.SetPlaces("organized", NoticePlaces.Windows);
        Assert.Equal(NoticePlaces.Windows, module.Context!.Notifier.PlacesFor("organized")); // the module sees it

        await _registry.SetInstalledAsync("downloads", false);
        Assert.Null(entry.Notifier); // not running: nothing to show on the page

        await _registry.SetInstalledAsync("downloads", true);
        Assert.Equal(NoticePlaces.Windows, entry.Notifier!.PlacesFor("organized")); // kept across reinstall
    }
    // ---- Activation verbs

    [Fact]
    public async Task A_registered_verb_receives_its_values_and_goes_away_with_the_module()
    {
        IReadOnlyList<string>? received = null;
        await StartAsync("downloads", ctx => ctx.Activation.Register("organize", values => received = values));

        Assert.True(_services.Activation.TryRoute(["--organize", @"C:\Users\me\Downloads"]));
        Assert.Equal([@"C:\Users\me\Downloads"], received);
        Assert.False(_services.Activation.TryRoute(["--unknown"]));
        Assert.False(_services.Activation.TryRoute([]));
        Assert.False(_services.Activation.TryRoute(["organize"])); // not a verb without dashes

        await _registry.SetInstalledAsync("downloads", false);
        Assert.False(_services.Activation.TryRoute(["--organize", "x"]));
    }

    [Fact]
    public async Task Two_modules_cannot_claim_the_same_verb()
    {
        var first = FakeModule.Create("downloads");
        first.OnStart = ctx => ctx.Activation.Register("organize", _ => { });
        var second = FakeModule.Create("other");
        second.OnStart = ctx => ctx.Activation.Register("organize", _ => { });
        _registry.Register(first);
        _registry.Register(second);
        await _registry.StartAsync();
        await _registry.SetInstalledAsync("downloads", true);

        await _registry.SetInstalledAsync("other", true);

        Assert.IsType<InvalidOperationException>(_registry.Find("other")!.LastError);
        Assert.True(_registry.Find("downloads")!.IsRunning);
        Assert.True(_services.Activation.IsRegistered("organize")); // still the first module's
    }

    [Theory]
    [InlineData("")]
    [InlineData("--organize")]
    [InlineData("two words")]
    public async Task Malformed_verbs_are_rejected(string verb)
    {
        var module = await StartAsync("downloads");
        Assert.Throws<ArgumentException>(() => module.Context!.Activation.Register(verb, _ => { }));
    }

    // ---- Settings sections

    [Fact]
    public async Task A_settings_page_can_open_on_a_section()
    {
        await StartAsync("downloads", ctx => ctx.Ui.SetSettingsPage(section => section ?? "first"));
        var page = _registry.Find("downloads")!.SettingsPage!;

        Assert.Equal("history", page.CreateView("history"));
        Assert.Equal("first", page.CreateView());
    }

    [Fact]
    public async Task The_Phase_1_settings_page_overload_still_works()
    {
        await StartAsync("keep-awake", ctx => ctx.Ui.SetSettingsPage(() => "page"));
        Assert.Equal("page", _registry.Find("keep-awake")!.SettingsPage!.CreateView("ignored"));
    }

    [Fact]
    public void ShellIntegration_is_a_capability()
    {
        Assert.Equal("shell-integration", Capability.ShellIntegration.Id);
    }
}

/// <summary>Review fixes on f040861 (Karl): isolation, notice lifetimes, malformed notice settings.</summary>
public sealed class ModuleServicesHardeningTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>Timers fire only when told to.</summary>
    private sealed class ManualTime : TimeProvider
    {
        public List<ManualTimer> Timers { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        public bool Disposed { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task An_unusable_data_folder_fails_that_module_only()
    {
        string root = _dir.File("data");
        File.WriteAllText(root, "a file where the data root should be");
        var services = new ModuleServices { DataRoot = root };
        var registry = new ModuleRegistry(new ConfigStore(_dir.File("settings.json")), new IslandActivityHub(), NullLoggerFactory.Instance, services);
        var first = FakeModule.Create("first", PresetMembership.Essentials);
        var second = FakeModule.Create("second", PresetMembership.Essentials);
        registry.Register(first);
        registry.Register(second);

        await registry.StartAsync(); // first run installs both; neither can make its folder

        Assert.True(registry.Find("first")!.IsInstalled);
        Assert.NotNull(registry.Find("first")!.LastError);
        Assert.NotNull(registry.Find("second")!.LastError); // the loop reached the second module
        Assert.Equal(0, first.Starts);
    }

    [Fact]
    public async Task An_old_lifetimes_expiry_never_clears_a_reinstalled_modules_notice()
    {
        var time = new ManualTime();
        var island = new IslandActivityHub();
        var services = new ModuleServices { DataRoot = _dir.File("data"), Time = time };
        var registry = new ModuleRegistry(new ConfigStore(_dir.File("settings.json")), island, NullLoggerFactory.Instance, services);
        var module = FakeModule.Create("downloads");
        module.OnStart = ctx => ctx.Notifier.Declare([new NoticeKind("organized", "Organized", NoticePlaces.Island)]);
        registry.Register(module);
        await registry.StartAsync();
        await registry.SetInstalledAsync("downloads", true);

        module.Context!.Notifier.Show(new Notice("organized", "old"));
        var oldTimer = time.Timers[^1];
        await registry.SetInstalledAsync("downloads", false); // stop disposes the old lifetime's timers
        await registry.SetInstalledAsync("downloads", true);
        module.Context!.Notifier.Show(new Notice("organized", "new"));

        oldTimer.Fire(); // as if it was already running when the module stopped
        Assert.Equal("new", island.Current!.Title);
        Assert.True(oldTimer.Disposed);

        time.Timers[^1].Fire(); // the new lifetime's own expiry still works
        Assert.Null(island.Current);
    }

    [Fact]
    public async Task Null_notice_settings_mean_defaults_and_never_break_Show()
    {
        var island = new IslandActivityHub();
        var config = new ConfigStore(_dir.File("settings.json"));
        File.WriteAllText(config.FilePath, """{ "version": 1, "modules": { "downloads": { "notices": null } } }""");
        config.Load();
        var registry = new ModuleRegistry(config, island, NullLoggerFactory.Instance, new ModuleServices { DataRoot = _dir.File("data") });
        var module = FakeModule.Create("downloads");
        module.OnStart = ctx => ctx.Notifier.Declare([new NoticeKind("organized", "Organized", NoticePlaces.Island)]);
        registry.Register(module);
        await registry.StartAsync();
        await registry.SetInstalledAsync("downloads", true);
        var notifier = module.Context!.Notifier;

        Assert.Equal(NoticePlaces.Island, notifier.PlacesFor("organized"));
        notifier.Show(new Notice("organized", "4 files organized"));
        Assert.Equal("4 files organized", island.Current!.Title);

        notifier.SetPlaces("organized", NoticePlaces.None);
        Assert.Equal(NoticePlaces.None, notifier.PlacesFor("organized"));
    }
}
public sealed class SingleInstanceArgumentsTests
{
    [Fact]
    public void A_second_launch_delivers_its_arguments()
    {
        var name = "ChippiTest-" + Guid.NewGuid().ToString("N");
        using var first = SingleInstance.TryAcquire(name);
        var received = new System.Collections.Concurrent.BlockingCollection<IReadOnlyList<string>>();
        first!.ActivationRequested += (_, e) => received.Add(e.Arguments);

        Assert.Null(SingleInstance.TryAcquire(name, ["--organize", @"C:\Users\me\Downloads"]));
        Assert.Null(SingleInstance.TryAcquire(name));

        Assert.True(received.TryTake(out var organize, TimeSpan.FromSeconds(5)));
        Assert.Equal(["--organize", @"C:\Users\me\Downloads"], organize);
        Assert.True(received.TryTake(out var plain, TimeSpan.FromSeconds(5)));
        Assert.Empty(plain);
    }

    [Fact]
    public async Task Requests_before_anyone_subscribes_are_held_in_order()
    {
        var name = "ChippiTest-" + Guid.NewGuid().ToString("N");
        using var first = SingleInstance.TryAcquire(name);
        Assert.Null(SingleInstance.TryAcquire(name, ["--a"]));
        Assert.Null(SingleInstance.TryAcquire(name, ["--b"]));
        await Task.Delay(300);

        var received = new List<string>();
        first!.ActivationRequested += (_, e) => received.Add(e.Arguments[0]);

        Assert.Equal(["--a", "--b"], received);
    }
}
