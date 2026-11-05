// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.QuickActions;

/// <summary>A power plan, as Windows names it.</summary>
public sealed record PowerPlan(Guid Id, string Name, bool Active);

/// <summary>A drive Windows can eject: a USB stick, an external disk, an SD card.</summary>
/// <param name="Id">Identifies it to <see cref="IQuickSystem.EjectAsync"/>.</param>
/// <param name="Name">As Explorer shows it: "Samsung T7 (E:)".</param>
/// <param name="Size">Its capacity in bytes.</param>
public sealed record RemovableDrive(string Id, string Name, long Size);

/// <summary>An app with a window you can switch to (as Alt+Tab lists them), one per program file.</summary>
/// <param name="Path">The program file: it keeps an app on the kept list and gives its icon.</param>
/// <param name="Name">Its name, from the file's description ("Google Chrome"), else the file's name.</param>
/// <param name="Front">The app that was in front before the island opened.</param>
/// <param name="NotResponding">Its window has stopped responding: Quit apps marks it, and its X ends it.</param>
public sealed record OpenApp(string Path, string Name, bool Front, bool NotResponding = false);

public enum PowerChoice { Sleep, Restart, ShutDown, SignOut }

/// <summary>Everything the page shows, read at once.</summary>
/// <param name="On">Each on/off toggle this PC has, by its state now (Plan isn't one: its state is the active plan).</param>
/// <param name="Unsupported">What this PC can't do: no Bluetooth or Wi-Fi radio, no camera, one screen only.</param>
/// <param name="Plans">The power plans, the active one marked.</param>
/// <param name="Drives">The drives that can be ejected.</param>
/// <param name="Apps">The open apps, in the order Alt+Tab has them.</param>
/// <param name="Frozen">How many apps have stopped responding.</param>
/// <param name="RecycleBinItems">How many items are in the Recycle Bin, on every drive.</param>
public sealed record QuickState(
    IReadOnlyDictionary<QuickActionId, bool> On,
    IReadOnlySet<QuickActionId> Unsupported,
    IReadOnlyList<PowerPlan> Plans,
    IReadOnlyList<RemovableDrive> Drives,
    IReadOnlyList<OpenApp> Apps,
    int Frozen,
    long RecycleBinItems)
{
    public static QuickState Empty { get; } = new(new Dictionary<QuickActionId, bool>(), new HashSet<QuickActionId>(), [], [], [], 0, 0);
}

/// <summary>
/// What Windows does for Quick actions. <see cref="Windows.WindowsQuickSystem"/> acts on the PC; tests use a fake, so
/// nothing in a test locks, sleeps or changes the owner's PC. Every method may be called from the UI thread and does its
/// slow work off it; a failure throws.
/// </summary>
public interface IQuickSystem
{
    /// <summary>Reads every state at once. <paramref name="frontProcessId"/> is the app in front before the island opened, or 0.</summary>
    Task<QuickState> ReadAsync(uint frontProcessId, CancellationToken ct = default);

    /// <summary>Turns an on/off toggle on or off: Dark through Camera.</summary>
    Task SetAsync(QuickActionId id, bool on);

    Task SetPowerPlanAsync(Guid plan);

    Task EjectAsync(RemovableDrive drive);

    /// <summary>Asks every window of the app to close, as its close button does: apps with unsaved work still ask to save.</summary>
    Task QuitAsync(OpenApp app);

    /// <summary>Ends one app that has stopped responding, since a hung app can't answer a close.</summary>
    Task EndAsync(OpenApp app);

    /// <summary>Ends every app that has stopped responding.</summary>
    Task EndFrozenAppsAsync();

    /// <summary>Runs an action: Lock, Displays, ScreenSaver, Recycle or Explorer.</summary>
    Task RunAsync(QuickActionId id);

    Task PowerAsync(PowerChoice choice);
}
