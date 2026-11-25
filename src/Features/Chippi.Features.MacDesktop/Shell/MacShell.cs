// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;

namespace Chippi.Features.MacDesktop.Shell;

/// <summary>
/// What the menu bar and the dock need from Chippi: the shared action PC (they never send keys, move windows or start
/// programs themselves), Settings, the notch and the island's activities.
/// </summary>
internal sealed class MacShell(ModuleContext context, IslandActivityHub? activities)
{
    public ModuleContext Context { get; } = context;

    public IActionPc? Pc => Context.Actions;

    public ActionRunner? Runner { get; } = context.Actions is { } pc
        ? new ActionRunner(pc, context.Commands, () => null, _ => "")
        : null;

    /// <summary>The island's activities, for the menu bar's badges; null in tests.</summary>
    public IslandActivityHub? Activities { get; } = activities;

    public PetColor PetColor => Context.ChippiColor();

    /// <summary>Opens the notch's home (the menu bar's Controls). Set by the shell.</summary>
    public Action OpenNotchHome { get; set; } = () => { };

    /// <summary>The notch on a display's bar: its monitor and left and right edges in physical pixels; null without one.</summary>
    public Func<(nint Monitor, double Left, double Right)?> Notch { get; set; } = () => null;

    /// <summary>Runs keys in the app in front through the shared PC (Win+A, Win+N…), logging a refusal.</summary>
    public async Task PressAsync(string keys)
    {
        if (Pc is not { } pc || KeyCombo.Parse(keys) is not { } combo)
            return;
        try { await pc.PressAsync(combo); }
        catch (ActionFailure ex) { Chippi.Core.Diagnostics.Log.Warn($"Mac-style desktop: {keys} failed", ex); }
    }

    /// <summary>Presses one key alone (the Windows key for Start).</summary>
    public async Task TapAsync(uint vk)
    {
        if (Pc is not { } pc)
            return;
        try { await pc.PressAsync(new KeyCombo(KeyModifiers.None, vk)); }
        catch (ActionFailure ex) { Chippi.Core.Diagnostics.Log.Warn("Mac-style desktop: a key failed", ex); }
    }

    /// <summary>A Window action on <paramref name="window"/>, through the shared PC.</summary>
    public void Window(nint window, WindowAction action)
    {
        if (Pc is not { } pc || window == 0)
            return;
        try
        {
            if (pc.CaptureWindow(window) is { } captured)
                pc.Window(captured, action);
            else
                pc.Window(window, action);
        }
        catch (ActionFailure ex) { Chippi.Core.Diagnostics.Log.Warn($"Mac-style desktop: {action} failed", ex); }
    }
}
