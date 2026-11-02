// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Windows.Devices.Radios;

namespace Chippi.Features.QuickActions.Windows;

/// <summary>The Bluetooth and Wi-Fi radios. The list is kept for a minute, so a page read only asks each radio its state.</summary>
internal sealed class WirelessRadios
{
    private readonly Refreshed<IReadOnlyList<Radio>> _radios = new(LoadAsync, TimeSpan.FromMinutes(1));

    public Task<IReadOnlyList<Radio>> GetAsync() => _radios.GetAsync();

    /// <summary>On when any radio of the kind is on; null when this PC has none.</summary>
    public static bool? IsOn(IReadOnlyList<Radio> radios, RadioKind kind)
    {
        bool any = false;
        foreach (var radio in radios)
        {
            if (radio.Kind != kind)
                continue;
            if (radio.State == RadioState.On)
                return true;
            any = true;
        }
        return any ? false : null;
    }

    public async Task SetAsync(RadioKind kind, bool on)
    {
        var radios = (await _radios.GetAsync()).Where(r => r.Kind == kind).ToList();
        if (radios.Count == 0)
            throw new InvalidOperationException($"{kind} failed: this PC has no {kind} radio");
        try
        {
            foreach (var radio in radios)
            {
                var access = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
                if (access != RadioAccessStatus.Allowed)
                    throw new InvalidOperationException($"{kind} failed: Windows answered {access}");
            }
        }
        catch
        {
            _radios.Expire(); // a radio may have gone; read them again next time
            throw;
        }
    }

    private static async Task<IReadOnlyList<Radio>> LoadAsync()
    {
        await Radio.RequestAccessAsync();
        return await Radio.GetRadiosAsync();
    }
}
