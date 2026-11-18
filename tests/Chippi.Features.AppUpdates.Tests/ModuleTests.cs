// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Actions;
using Chippi.Core.Admin;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.AppUpdates.Tests;

public sealed class ModuleTests
{
    [Fact]
    public void Start_is_lazy_registers_the_shared_apps_door_and_releases_shortcuts() => WpfTestThread.Run(() =>
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Source.Checks);
        Assert.Equal(0, fixture.WindowReads);
        Assert.False(fixture.Ui.Tile!.Spec.OnHome);
        Assert.Equal(AppUpdatesModule.Page, fixture.Ui.Tile.Spec.Key);
        Assert.Equal(AppUpdatesModule.ShortcutNames, fixture.Context.Shortcuts.List.Select(s => s.Name));
        var command = Assert.Single(fixture.Ui.Commands!(), c => c.Tile != null);
        Assert.Equal("apps", command.Tile!.Key); Assert.True(command.Tile.Menu);
        WpfTestThread.Wait(command.Run());
        Assert.Equal(AppUpdatesModule.Page, fixture.Opened);
        Assert.Equal(1, fixture.Source.Checks);
        Assert.Equal(0, fixture.Source.Updates);
        WpfTestThread.Wait(fixture.Module.StopAsync(default));
        Assert.Empty(fixture.Context.Shortcuts.List);
    });

    [Fact]
    public void Check_command_refreshes_even_when_the_page_is_already_open() => WpfTestThread.Run(() =>
    {
        using var fixture = new Fixture();
        var check = fixture.Ui.Commands!().Single(c => c.Key == "app-updates/check");
        WpfTestThread.Wait(check.Run());
        WpfTestThread.Wait(check.Run());
        Assert.Equal(2, fixture.Source.Checks);
        Assert.Equal(0, fixture.Source.Updates); Assert.Equal(0, fixture.Admin.Calls);
    });

    [Fact]
    public void Update_all_command_checks_and_opens_a_confirmation_without_updating() => WpfTestThread.Run(() =>
    {
        using var fixture = new Fixture();
        var command = fixture.Ui.Commands!().Single(c => c.Key == "app-updates/all");
        WpfTestThread.Wait(command.Run());
        Assert.Equal(1, fixture.Source.Checks); Assert.Equal(0, fixture.Source.Updates);
        Assert.Equal(0, fixture.Admin.Calls);
        var page = Assert.IsType<Views.AppUpdatesView>(fixture.Ui.Tile!.Spec.CreateSection());
        Assert.Equal(fixture.Source.Package.Id, Assert.Single(page.Confirmation!.Packages).Id);
    });

    [Theory]
    [InlineData(@"C:\fixture\app.exe", true)]
    [InlineData(@"c:\FIXTURE\APP.EXE", true)]
    [InlineData(@"C:\fixture\helper.exe", false)]
    [InlineData(@"C:\fixture-other\app.exe", false)]
    public void Close_matching_uses_exact_registered_executable_not_a_folder_or_name(string executable, bool match)
    {
        var package = new PackageUpdate("Fixture.App", "App", "1", "2", "winget", UpdateScope.Machine) { IconPath = @"C:\fixture\app.exe", InstallLocation = @"C:\fixture" };
        Assert.Equal(match, PackageWindowMatcher.Matches(package, executable));
        Assert.False(PackageWindowMatcher.Matches(package with { IconPath = null }, executable));
    }

    [Fact]
    public void Confirmed_soft_close_uses_shared_actions_and_never_forces_a_save_dialog() => WpfTestThread.Run(() =>
    {
        using var fixture = new Fixture();
        var window = new ListedWindow(41, 42, "App", "Draft", @"C:\fixture\app.exe", false, 0);
        fixture.Windows = [window];
        WpfTestThread.Wait(fixture.Module.Engine!.CheckAsync());
        var batch = fixture.Module.Engine.CaptureBatch();
        var task = fixture.Module.ExecuteBatchAsync(batch, true);
        WpfTestThread.Wait(Assert.ThrowsAsync<ActionFailure>(() => task));
        Assert.Equal(new[] { WindowAction.Close }, fixture.Pc.Actions);
        Assert.Equal(0, fixture.Admin.Calls); Assert.Equal(0, fixture.Source.Updates);
        fixture.Windows = [];
        WpfTestThread.Wait(fixture.Module.ExecuteBatchAsync(batch, false));
        Assert.Equal(1, fixture.Admin.Calls);
    });

    [Fact]
    public void Window_reused_for_another_app_is_not_closed() => WpfTestThread.Run(() =>
    {
        using var fixture = new Fixture();
        fixture.Windows = [new ListedWindow(41, 42, "App", "Draft", @"C:\fixture\app.exe", false, 0)];
        fixture.Pc.Executable = @"C:\different\app.exe";
        WpfTestThread.Wait(fixture.Module.Engine!.CheckAsync());
        WpfTestThread.Wait(Assert.ThrowsAsync<ActionFailure>(() => fixture.Module.ExecuteBatchAsync(fixture.Module.Engine.CaptureBatch(), true)));
        Assert.Empty(fixture.Pc.Actions); Assert.Equal(0, fixture.Admin.Calls);
    });

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "chippi-update-module-" + Guid.NewGuid().ToString("N"));
        public Source Source { get; } = new();
        public Admin Admin { get; } = new();
        public Pc Pc { get; } = new();
        public Ui Ui { get; } = new();
        public IReadOnlyList<ListedWindow> Windows { get; set; } = [];
        public int WindowReads;
        public string? Opened;
        public ModuleContext Context { get; }
        public AppUpdatesModule Module { get; }
        public Fixture()
        {
            Context = new(AppUpdatesModule.Id, new Settings(), NullLogger.Instance, new Island(), Ui)
            {
                DataDirectory = _directory, Admin = Admin, Actions = Pc,
                TryOpenIslandPage = page => { Opened = page; return true; },
            };
            Module = new(() => Source, () => { Interlocked.Increment(ref WindowReads); return Windows; }, _ => Task.CompletedTask);
            WpfTestThread.Wait(Module.StartAsync(Context, default));
        }
        public void Dispose()
        {
            WpfTestThread.Wait(Module.StopAsync(default));
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }
    private sealed class Source : IPackageSource
    {
        public int Checks, Updates;
        public PackageUpdate Package { get; } = new("Fixture.App", "App", "1", "2", "winget", UpdateScope.Machine) { IconPath = @"C:\fixture\app.exe" };
        public Task<IReadOnlyList<PackageUpdate>> CheckAsync(bool includeUnknown, CancellationToken ct = default) { Checks++; return Task.FromResult<IReadOnlyList<PackageUpdate>>([Package]); }
        public Task<PackageInstallResult> UpdateAsync(PackageUpdate package, IProgress<PackageInstallProgress>? progress = null, CancellationToken ct = default)
        { Updates++; return Task.FromResult(new PackageInstallResult(UpdateState.Updated)); }
    }
    private sealed class Admin : IAdminHelper
    {
        public int Calls;
        public Task<AdminPortResult> CloseProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminPortResult> EndProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminPortResult> StopServiceAsync(string serviceName, AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdminUpdateResult>> UpdatePackagesAsync(IReadOnlyList<AdminUpdateItem> items, IProgress<AdminUpdateProgress>? progress = null, CancellationToken ct = default)
        { Calls++; return Task.FromResult<IReadOnlyList<AdminUpdateResult>>(items.Select(i => new AdminUpdateResult(i.PackageId, "Done", 0)).ToArray()); }
    }
    private sealed class Settings : IModuleSettings
    { public T Get<T>() where T : class, new() => new(); public void Save<T>(T value) where T : class { } }
    private sealed class Ui : IModuleUi
    {
        public IslandTileContribution? Tile;
        public Func<IReadOnlyList<ChippiCommand>>? Commands;
        public IIslandTile AddIslandTile(IslandTileSpec spec) => Tile = new(AppUpdatesModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> createView) { }
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands = commands;
    }
    private sealed class Island : IIslandPublisher
    { public void Publish(IslandActivity activity) { } public void Clear(string key) { } public void ReportSuccess(string? message = null) { } }
    private sealed class Pc : IActionPc
    {
        public string Executable = @"C:\fixture\app.exe";
        public List<WindowAction> Actions { get; } = [];
        public bool? HasApp(string parsingName) => false;
        public bool Exists(string path) => false;
        public nint WindowUnderPointer() => 0;
        public nint WindowInFront() => 0;
        public void Open(string target) => throw new InvalidOperationException();
        public ActionWindow? CaptureWindow(nint window) => new(window, 42, 43, 1, "fixture", Executable, "App", "Draft", false);
        public void Window(nint window, WindowAction action) => Actions.Add(action);
        public void Media(MediaAction action) => throw new InvalidOperationException();
        public Task PressAsync(KeyCombo keys) => throw new InvalidOperationException();
        public Task TypeAsync(string text) => throw new InvalidOperationException();
        public void Run(CommandShell shell, string line) => throw new InvalidOperationException();
        public Task<(bool Terminal, string? Text)> TerminalSelectionAsync() => throw new InvalidOperationException();
    }
}
