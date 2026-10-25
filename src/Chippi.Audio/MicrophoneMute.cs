// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Audio;

public enum MicrophoneMuteState { NoMicrophone, Live, Muted, Mixed, Unavailable }

public sealed record MicrophoneMuteStatus(MicrophoneMuteState State, bool Latched, IReadOnlyList<string> Failed)
{
    public static MicrophoneMuteStatus Empty { get; } = new(MicrophoneMuteState.NoMicrophone, false, []);
}

/// <summary>Engine-thread mute latch. Owns only false-to-true changes, never a mic already muted.</summary>
internal sealed class MicrophoneMute
{
    private readonly HashSet<string> _owned = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private bool _latched;
    public MicrophoneMuteStatus Status { get; private set; } = MicrophoneMuteStatus.Empty;

    public void Toggle(IDeviceSystem system, DeviceCatalog catalog)
    {
        var mics = catalog.Active(AudioFlow.Capture);
        bool allMuted = mics.Count > 0;
        foreach (var mic in mics)
        {
            try { allMuted &= system.GetLevel(mic.Id).Muted; }
            catch { allMuted = false; }
        }
        _latched = !allMuted;
        _seen.Clear();
        var failed = new List<string>();
        foreach (var mic in mics)
            Set(system, mic.Id, _latched, failed);
        Refresh(system, catalog, failed);
    }

    public void Stop(IDeviceSystem system, DeviceCatalog catalog)
    {
        _latched = false;
        _seen.Clear();
        Refresh(system, catalog);
    }

    public void Refresh(IDeviceSystem system, DeviceCatalog catalog, List<string>? failures = null)
    {
        var failed = failures ?? [];
        var active = catalog.Active(AudioFlow.Capture);
        _seen.IntersectWith(active.Select(m => m.Id));
        foreach (var mic in active)
        {
            if (_latched && !_seen.Contains(mic.Id))
                Set(system, mic.Id, true, failed);
            else if (!_latched && _owned.Contains(mic.Id))
                Set(system, mic.Id, false, failed);
        }
        int muted = 0, readable = 0;
        foreach (var mic in active)
        {
            try
            {
                bool value = system.GetLevel(mic.Id).Muted;
                readable++;
                if (value) muted++;
                else _owned.Remove(mic.Id); // an external unmute gave it back
            }
            catch { failed.Add(mic.Id); }
        }
        Status = new(active.Count == 0 ? MicrophoneMuteState.NoMicrophone
            : readable != active.Count ? MicrophoneMuteState.Unavailable
            : muted == active.Count ? MicrophoneMuteState.Muted
            : muted == 0 ? MicrophoneMuteState.Live : MicrophoneMuteState.Mixed,
            _latched, failed.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private void Set(IDeviceSystem system, string id, bool muted, List<string> failed)
    {
        try
        {
            bool previous = system.GetLevel(id).Muted;
            if (previous != muted)
            {
                system.SetMute(id, muted);
                if (system.GetLevel(id).Muted != muted) { failed.Add(id); return; }
                if (muted) _owned.Add(id);
            }
            if (!muted) _owned.Remove(id);
            _seen.Add(id);
        }
        catch { failed.Add(id); }
    }
}