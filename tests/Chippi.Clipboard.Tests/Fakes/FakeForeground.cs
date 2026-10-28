// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Clipboard;
using Chippi.Core.Clipboard;

namespace Chippi.Clipboard.Tests;

/// <summary>A pretend foreground window. <see cref="OnPaste"/> plays the target reading the clipboard after Ctrl+V.</summary>
public sealed class FakeForeground : IForeground
{
    public static readonly PasteTarget Editor = new(0x1234, 4242);

    public PasteTarget? Front { get; set; } = Editor;

    public bool Alive { get; set; } = true;

    /// <summary>False plays an elevated or unreadable target.</summary>
    public bool CanSend { get; set; } = true;

    public bool ComesToFront { get; set; } = true;

    public int Pastes { get; private set; }

    public int Beeps { get; private set; }

    public Action? OnPaste { get; set; }

    public PasteTarget? Current() => Front;

    public bool IsAlive(PasteTarget target) => Alive;

    public bool CanSendInput(PasteTarget target) => CanSend;

    public Task<bool> BringToFrontAsync(PasteTarget target, TimeSpan timeout, CancellationToken cancel = default)
    {
        if (ComesToFront)
            Front = target;
        return Task.FromResult(ComesToFront);
    }

    public Task WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken cancel = default) => Task.CompletedTask;

    public void SendPaste()
    {
        Pastes++;
        OnPaste?.Invoke();
    }

    public void Beep() => Beeps++;
}
