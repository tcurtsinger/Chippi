// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using Chippi.Core.Diagnostics;
using Windows.Devices.Radios;

namespace Chippi.Features.QuickActions.Windows;

/// <summary>
/// Quick actions on this PC. A read takes a few milliseconds and touches no disk: what can't change quickly (the radios,
/// whether a camera exists, each drive's device, name and size) is kept between reads, and the Recycle Bin is counted
/// at most every 30 seconds. A part that fails to read is left out and logged once.
/// </summary>
public sealed class WindowsQuickSystem : IQuickSystem
{
    /// <summary>How long a read waits on a slow lookup (WinRT, the Recycle Bin) before leaving its part out.</summary>
    private static readonly TimeSpan SlowWait = TimeSpan.FromSeconds(3);

    private readonly WirelessRadios _radios = new();
    private readonly CameraAccess _camera = new();
    private readonly RemovableDrives _drives = new();
    private readonly AltTabApps _apps = new();
    private readonly Refreshed<long> _recycled = new(() => Task.FromResult(SystemActions.RecycleBinItems()), TimeSpan.FromSeconds(30));
    private readonly ConcurrentDictionary<string, byte> _warned = new();

    public Task<QuickState> ReadAsync(uint frontProcessId, CancellationToken ct = default) => Task.Run(async () =>
    {
        var on = new Dictionary<QuickActionId, bool>();
        var unsupported = new HashSet<QuickActionId>();

        ReadPart("dark mode", () => on[QuickActionId.Dark] = ShellToggles.Dark);
        ReadPart("hidden files", () => on[QuickActionId.Hidden] = ShellToggles.Hidden);
        ReadPart("desktop icons", () => on[QuickActionId.Icons] = ShellToggles.Icons);
        ReadPart("taskbar auto-hide", () =>
        {
            if (ShellToggles.AutoHide is { } autoHide)
                on[QuickActionId.AutoHide] = autoHide;
        });
        ReadPart("the screens", () =>
        {
            var (screens, extended) = DisplayTopology.Read();
            if (screens < 2)
                unsupported.Add(QuickActionId.Extend);
            else
                on[QuickActionId.Extend] = extended;
        });
        await ReadPartAsync("the radios", async () =>
        {
            var radios = await _radios.GetAsync().WaitAsync(SlowWait, ct);
            AddRadio(QuickActionId.Bluetooth, WirelessRadios.IsOn(radios, RadioKind.Bluetooth));
            AddRadio(QuickActionId.WiFi, WirelessRadios.IsOn(radios, RadioKind.WiFi));
        }, ct);
        await ReadPartAsync("the camera", async () =>
        {
            if (await _camera.PresentAsync().WaitAsync(SlowWait, ct))
                on[QuickActionId.Camera] = CameraAccess.Allowed;
            else
                unsupported.Add(QuickActionId.Camera);
        }, ct);
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<PowerPlan> plans = [];
        IReadOnlyList<RemovableDrive> drives = [];
        IReadOnlyList<OpenApp> apps = [];
        int frozen = 0;
        long recycled = 0;
        ReadPart("power plans", () => plans = PowerPlans.Read());
        ReadPart("drives", () => drives = _drives.Read());
        ReadPart("open apps", () =>
        {
            var windows = AltTabApps.List();
            apps = _apps.Apps(windows, frontProcessId);
            frozen = AltTabApps.Frozen(windows).Count;
        });
        await ReadPartAsync("the Recycle Bin", async () => recycled = await _recycled.GetAsync().WaitAsync(SlowWait, ct), ct);
        return new QuickState(on, unsupported, plans, drives, apps, frozen, recycled);

        void AddRadio(QuickActionId id, bool? state)
        {
            if (state is { } isOn)
                on[id] = isOn;
            else
                unsupported.Add(id);
        }
    }, ct);

    public Task SetAsync(QuickActionId id, bool on) => Task.Run(async () =>
    {
        switch (id)
        {
            case QuickActionId.Dark: ShellToggles.SetDark(on); break;
            case QuickActionId.Hidden: ShellToggles.SetHidden(on); break;
            case QuickActionId.Icons: ShellToggles.SetIcons(on); break;
            case QuickActionId.AutoHide: ShellToggles.SetAutoHide(on); break;
            case QuickActionId.Extend: DisplayTopology.Set(on); break;
            case QuickActionId.Bluetooth: await _radios.SetAsync(RadioKind.Bluetooth, on); break;
            case QuickActionId.WiFi: await _radios.SetAsync(RadioKind.WiFi, on); break;
            case QuickActionId.Camera: CameraAccess.Set(on); break;
            default: throw new ArgumentOutOfRangeException(nameof(id), id, "Not an on/off toggle");
        }
    });

    public Task SetPowerPlanAsync(Guid plan) => Task.Run(() => PowerPlans.SetActive(plan));

    public Task EjectAsync(RemovableDrive drive) => Task.Run(() => RemovableDrives.Eject(drive));

    public Task QuitAsync(OpenApp app) => Task.Run(() => AltTabApps.Quit(app.Path));

    public Task EndFrozenAppsAsync() => Task.Run(AltTabApps.EndFrozen);

    public Task RunAsync(QuickActionId id) => id switch
    {
        QuickActionId.Lock => Task.Run(SystemActions.Lock),
        QuickActionId.Displays => Task.Run(SystemActions.TurnOffDisplaysAsync),
        QuickActionId.ScreenSaver => Task.Run(SystemActions.StartScreenSaver),
        QuickActionId.Recycle => EmptyRecycleBinAsync(),
        QuickActionId.Explorer => Task.Run(ExplorerShell.Restart),
        _ => Task.FromException(new ArgumentOutOfRangeException(nameof(id), id, "Not an action Windows runs")),
    };

    public Task PowerAsync(PowerChoice choice) => Task.Run(() => SystemActions.Power(choice));

    private async Task EmptyRecycleBinAsync()
    {
        try
        {
            await SystemActions.EmptyRecycleBinAsync();
        }
        finally
        {
            _recycled.Expire();
        }
    }

    private void ReadPart(string part, Action read)
    {
        try
        {
            read();
        }
        catch (Exception ex)
        {
            Warn(part, ex);
        }
    }

    private async Task ReadPartAsync(string part, Func<Task> read, CancellationToken ct)
    {
        try
        {
            await read();
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            Warn(part, ex);
        }
    }

    private void Warn(string part, Exception ex)
    {
        if (_warned.TryAdd(part, 0))
            Log.Warn($"Quick actions: reading {part} failed", ex);
    }
}
