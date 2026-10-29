// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.Sound;

/// <summary>
/// A volume slider's writes: one at a time, and while one runs only the newest waits. A drag steps the
/// slider many times a second, and each write re-reads every device and app on the audio thread, so
/// writing every step left the volume trailing the pointer. Call on the UI thread.
/// </summary>
internal sealed class LatestWrite
{
    private Func<Task>? _next;
    private bool _writing;

    public async void Send(Func<Task> write)
    {
        _next = write;
        if (_writing)
            return;
        _writing = true;
        try
        {
            while (_next is { } next)
            {
                _next = null;
                try { await next(); }
                catch { /* the module shows the error */ }
            }
        }
        finally { _writing = false; }
    }
}
