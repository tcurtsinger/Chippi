// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Scratchpad.Tests;

public sealed class ScratchpadModuleTests
{
    private sealed class Settings : IModuleSettings
    {
        public T Get<T>() where T : class, new() => throw new InvalidOperationException("Notes must not use settings");
        public void Save<T>(T value) where T : class => throw new InvalidOperationException("Notes must not use settings");
    }
    private sealed class Ui : IModuleUi
    {
        public IslandTileSpec Spec = null!;
        public IIslandTile AddIslandTile(IslandTileSpec spec) { Spec = spec; return new IslandTileContribution("scratchpad", spec); }
        public void SetSettingsPage(Func<string?, object> view) => throw new InvalidOperationException("No Scratchpad Settings page");
    }
    private sealed class Store : IScratchpadStore
    {
        public ScratchpadDocument Document = ScratchpadDocument.Empty();
        public ScratchpadDocument Load() => Document;
        public void Save(ScratchpadDocument doc) => Document = doc;
        public void Dispose() { }
    }
    [Fact]
    public void Module_defers_opening_storage_and_supplies_reusable_header_and_focus_factories() => WpfTestThread.Run(() =>
    {
        var ui = new Ui(); int opens = 0;
        var module = new ScratchpadModule(_ => { opens++; return new Store(); });
        var context = new ModuleContext("scratchpad", new Settings(), NullLogger.Instance, new IslandActivityHub().CreatePublisher("scratchpad"), ui);
        module.StartAsync(context, default).GetAwaiter().GetResult();
        Assert.Equal(0, opens);
        Assert.Equal(440, ui.Spec.PageWidth); Assert.Equal(80, ui.Spec.Order);
        var page = Assert.IsType<ScratchpadPage>(ui.Spec.CreateSection());
        Assert.Same(page.Header, ui.Spec.CreateHeader!()); Assert.Same(page.InitialFocus, ui.Spec.InitialFocus!());
        Assert.Same(page, ui.Spec.CreateSection());
        module.StopAsync(default).GetAwaiter().GetResult();
        Assert.Equal(1, opens);
        ((IFeatureModule)module).UninstallAsync(default).GetAwaiter().GetResult();
    });
}
