// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.App.Shell;
using Chippi.Core.Modules;

namespace Chippi.App.Flyout;

/// <summary>
/// Debug mode only (<c>--debug</c> or <c>CHIPPI_DEBUG=1</c>): a fake module with three Tools tiles,
/// so the flyout grid can be seen and toggled before Keep awake (WS4) lands. It is in the
/// Essentials preset only so a fresh dev profile installs it; it never registers outside debug mode.
/// </summary>
internal sealed class DebugSampleModule : IFeatureModule
{
    public const string ModuleId = "debug-sample-tiles";

    /// <summary>Registers the sample module when debug mode is on. Must run before the registry starts.</summary>
    public static void RegisterIfDebug(ShellContext shell)
    {
        if (shell.DebugMode)
            shell.Registry.Register(new DebugSampleModule());
    }

    public FeatureDescriptor Descriptor { get; } = new(ModuleId, "Sample tiles", "Debug-only tiles for checking the flyout grid", FeatureGroup.System)
    {
        Presets = PresetMembership.Essentials,
        Icon = "Wrench",
        Badges = [FeatureBadge.Needs("Debug only")],
    };

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        context.Ui.AddToolTile(Toggle("Toggle", "Zap", on: false, onLine: "On", offLine: "Off"));
        context.Ui.AddToolTile(Toggle("Timer", "Clock", on: true, onLine: "Until 6:00 PM", offLine: "Off"));
        context.Ui.AddToolTile(Toggle("A long sample name that trims", "Keyboard", on: false, onLine: "Ctrl+Alt+K", offLine: "Ctrl+Alt+K"));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static ToolTileModel Toggle(string name, string icon, bool on, string onLine, string offLine)
    {
        ToolTileModel? tile = null;
        tile = new ToolTileModel(name, icon, _ =>
        {
            tile!.IsOn = !tile.IsOn;
            tile.StateLine = tile.IsOn ? onLine : offLine;
            return Task.CompletedTask;
        })
        {
            IsOn = on,
            StateLine = on ? onLine : offLine,
        };
        return tile;
    }
}
