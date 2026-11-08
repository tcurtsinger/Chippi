// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;

namespace Chippi.Features.Shortcuts;

/// <summary>
/// The Sandbox probe's action host (<c>CHIPPI_SHORTCUTS_PROBE</c> names its file): runs nothing, and writes one line per fired
/// binding, "name|target". It proves a door fired and kept its input without needing a window to act on.
/// </summary>
public sealed class ProbeActionHost(string path) : IActionHost
{
    private readonly object _gate = new();

    public bool CapsLockOn => Console.CapsLock;

    public Task RunAsync(ChippiAction action, WindowTarget target)
    {
        lock (_gate)
            File.AppendAllText(path, $"{action.Name}|{target}{Environment.NewLine}");
        return Task.CompletedTask;
    }

    public bool IsMissing(ChippiAction action) => false;
}
