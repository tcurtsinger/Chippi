// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Diagnostics;

namespace Chippi.Features.CleaningMode;

/// <summary>The black screens. The real ones are windows (<see cref="Screens.CleaningScreens"/>); tests use a fake.</summary>
public interface ICleaningScreens
{
    /// <summary>Black on every screen, and on the main one Chippi, the hint, the dots and Unlock; faded in unless motion is reduced.</summary>
    void Show();

    /// <summary>The dots: <paramref name="presses"/> filled. <paramref name="counted"/>: this press added one, so Chippi squishes.</summary>
    void Count(int presses, bool counted);

    /// <summary>The dots full, Chippi rises and celebrates, and 700ms later the black fades; <paramref name="gone"/> runs once it has.</summary>
    void Unlock(Action gone);

    /// <summary>Gone at once: Windows took over.</summary>
    void Close();

    /// <summary>Unlock was clicked.</summary>
    event Action? UnlockClicked;

    /// <summary>Windows took over: the PC locked or went to sleep, or Ctrl+Alt+Del was pressed.</summary>
    event Action? Interrupted;
}

/// <summary>
/// One Cleaning mode (docs/phase-5.5/cleaning-mode.md): the screens black, then the keyboard locked, until Esc three times
/// or Unlock, or until Windows takes over. The keyboard comes back the moment it unlocks; the black follows after Chippi's
/// celebration. Whatever fails, the keyboard comes back and the black goes. Call on the UI thread.
/// </summary>
internal sealed class CleaningSession(IKeyboardLock keyboard, Func<ICleaningScreens> createScreens)
{
    private IDisposable? _lock;
    private ICleaningScreens? _screens;
    private UnlockCount? _count;
    private bool _unlocking;

    /// <summary>Whether Cleaning mode is on: from the start until the black has gone.</summary>
    public bool Active => _screens is not null;

    /// <summary>It started or ended.</summary>
    public event Action? Changed;

    /// <summary>Starts it; throws, with nothing left locked or shown, when the screens or the lock can't start.</summary>
    public void Start()
    {
        if (Active)
            return;
        _count = new UnlockCount();
        _unlocking = false;
        var screens = _screens = createScreens();
        screens.UnlockClicked += Unlock;
        screens.Interrupted += End;
        try
        {
            // The black first, so building it never holds up a key the lock should take.
            screens.Show();
            _lock = keyboard.Lock(OnKey);
        }
        catch
        {
            End();
            throw;
        }
        Changed?.Invoke();
    }

    private void OnKey(LockedKey key)
    {
        if (_count is null || _unlocking)
            return;
        int presses = _count.Key(key.Vk, key.Down, key.Time); // a key coming up only tells a new press from a held one's repeat
        if (!key.Down)
            return;
        if (_count.Unlocked)
            Unlock();
        else
            _screens?.Count(presses, counted: presses > 0); // a press that didn't count started the count over
    }

    /// <summary>Unlocks: the keyboard at once, then Chippi celebrates and the black fades.</summary>
    public void Unlock()
    {
        if (_screens is not { } screens || _unlocking)
            return;
        _unlocking = true;
        _lock?.Dispose();
        _lock = null;
        try
        {
            screens.Unlock(() =>
            {
                if (!ReferenceEquals(_screens, screens))
                    return;
                Detach();
                Changed?.Invoke();
            });
        }
        catch (Exception ex)
        {
            Log.Warn("Cleaning mode: unlocking failed", ex);
            End();
        }
    }

    /// <summary>Ends at once, nothing shown: Windows took over, or the feature stopped.</summary>
    public void End()
    {
        _lock?.Dispose();
        _lock = null;
        if (_screens is not { } screens)
            return;
        try { screens.Close(); }
        finally
        {
            Detach();
            Changed?.Invoke();
        }
    }

    private void Detach()
    {
        if (_screens is { } screens)
        {
            screens.UnlockClicked -= Unlock;
            screens.Interrupted -= End;
        }
        _screens = null;
        _count = null;
        _unlocking = false;
    }
}
