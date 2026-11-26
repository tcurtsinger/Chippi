// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.MacDesktop.MenuBar;

namespace Chippi.Notifications.Tests;

/// <summary>
/// The Mac-style menu bar's items run the features' own commands and pages by key, and the feature that names them isn't
/// one the menu bar's project references: this holds its keys against theirs, so a renamed command isn't a dead button.
/// </summary>
public sealed class MenuBarItemKeysTests
{
    private static string Target(string id) => BarItems.Press(id).Target;

    private static string? Feature(string id) => BarItems.Find(id)!.Feature;

    [Fact]
    public void Each_item_names_its_features_own_command_or_page()
    {
        Assert.Equal(Chippi.Features.CommandBar.CommandBarModule.OpenCommand, BarItems.SearchCommand);
        Assert.Equal(Chippi.Features.CommandBar.CommandBarModule.TileKey, Target(BarItems.Search));
        Assert.Equal(Chippi.Features.Clipboard.ClipboardFeatures.TileKey, Target(BarItems.Clipboard));
        Assert.Equal(Chippi.Features.Recording.RecordingModule.Page, Target(BarItems.Record));
        Assert.Equal(Chippi.Features.Recording.RecordingModule.Id + "/stop", BarItems.StopRecordingCommand);
        Assert.Equal(Chippi.Features.ScreenCapture.ScreenCaptureModule.Id + "/region", Target(BarItems.Capture));
        Assert.Equal(Chippi.Features.Sound.Mute.MicMuteModule.Id + "/toggle", Target(BarItems.Microphone));
        Assert.Equal(Chippi.Features.ScreenText.ScreenTextModule.Id + "/copy", Target(BarItems.CopyText));
        Assert.Equal(Chippi.Features.ColorPicker.ColorPickerModule.Id + "/pick", Target(BarItems.PickColor));
        Assert.Equal(Chippi.Features.KeepAwake.KeepAwakeModule.TileKey + "/toggle", Target(BarItems.KeepAwake));
        Assert.Equal(Chippi.Features.Displays.DisplaysModule.Id, Target(BarItems.Displays));
        Assert.Equal(Chippi.Features.SystemMonitor.SystemMonitorModule.TileKey, Target(BarItems.System));
        Assert.Equal(Chippi.Features.AgentUsage.AgentUsageModule.TileKey, Target(BarItems.Agents));
        Assert.Equal(Chippi.Features.QuickActions.QuickActionsModule.TileKey, Target(BarItems.Actions));
        Assert.Equal(Chippi.Features.Shelf.ShelfModule.Id, Target(BarItems.Shelf));
    }

    [Fact]
    public void Each_item_needs_the_feature_that_runs_it()
    {
        Assert.Equal(Chippi.Features.CommandBar.CommandBarModule.Id, Feature(BarItems.Search));
        Assert.Equal(Chippi.Features.Clipboard.ClipboardHistoryModule.Id, Feature(BarItems.Clipboard));
        Assert.Equal(Chippi.Features.AgentUsage.AgentUsageModule.Id, Feature(BarItems.Agents));
        Assert.Equal(Chippi.Features.ScreenCapture.ScreenCaptureModule.Id, Feature(BarItems.Capture));
        Assert.Equal(Chippi.Features.Recording.RecordingModule.Id, Feature(BarItems.Record));
        Assert.Equal(Chippi.Features.Sound.Mute.MicMuteModule.Id, Feature(BarItems.Microphone));
        Assert.Equal(Chippi.Features.ScreenText.ScreenTextModule.Id, Feature(BarItems.CopyText));
        Assert.Equal(Chippi.Features.ColorPicker.ColorPickerModule.Id, Feature(BarItems.PickColor));
        Assert.Equal(Chippi.Features.Displays.DisplaysModule.Id, Feature(BarItems.Displays));
        Assert.Equal(Chippi.Features.Shelf.ShelfModule.Id, Feature(BarItems.Shelf));
        Assert.Equal(Chippi.Features.QuickActions.QuickActionsModule.Id, Feature(BarItems.Actions));
        Assert.Equal(Chippi.App.Island.IslandHost.KeepAwakeModuleId, Feature(BarItems.KeepAwake));
    }
}
