// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Threading;
using Chippi.Core.Input;

namespace Chippi.Features.Debounce;

/// <summary>
/// Takes the next key pressed, for "Add key": the hub's last filter keeps it from the apps and gives its code to
/// <paramref name="picked"/> on the calling (UI) thread. Esc, or 10 seconds, ends it with null.
/// </summary>
internal sealed class KeyPicker : IInputFilter, IDisposable
{
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly Action<uint?> _picked;
    private readonly IInputRegistration _registration;
    private readonly Timer _limit;
    private int _done;

    public KeyPicker(IInputHub hub, Action<uint?> picked)
    {
        _picked = picked;
        _registration = hub.Add(this);
        _limit = new Timer(_ => Finish(null), null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
    }

    public InputOrder Order => InputOrder.Recorder;

    public InputDevices Devices => InputDevices.Keyboard;

    public bool Pauses => false;

    public bool Mouse(in MouseInput mouse) => false;

    public bool Key(in KeyInput key)
    {
        if (!key.Down || _done != 0)
            return false;
        uint vk = key.Vk;
        // Esc alone cancels; a lone modifier isn't a key worth a window of its own.
        if (vk == 0x1B)
            Finish(null);
        else
            Finish(vk);
        return true;
    }

    private void Finish(uint? vk)
    {
        if (Interlocked.Exchange(ref _done, 1) != 0)
            return;
        _ui.BeginInvoke(() =>
        {
            Dispose();
            _picked(vk);
        });
    }

    public void Dispose()
    {
        _limit.Dispose();
        _registration.Dispose();
    }
}
