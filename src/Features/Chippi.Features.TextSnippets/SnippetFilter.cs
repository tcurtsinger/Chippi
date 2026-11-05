// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Input;

namespace Chippi.Features.TextSnippets;

/// <summary>What the filter reads from Windows: the window in front, held modifiers and what a key types. Tests pass a fake.</summary>
internal interface IKeyReader
{
    nint Foreground();

    /// <summary>Win, or Ctrl or Alt alone, is down: a shortcut, not typing.</summary>
    bool CommandHeld();

    /// <summary>The character the key types now, or '\0' for none (a dead key, a control).</summary>
    char CharOf(uint vk, uint scan);
}

internal sealed class WindowsKeyReader : IKeyReader
{
    public nint Foreground() => Native.GetForegroundWindow();

    public bool CommandHeld() => Native.CommandHeld();

    public char CharOf(uint vk, uint scan) => Native.CharOf(vk, scan);
}

/// <summary>
/// The hub's last filter (docs/phase-6/text-snippets.md, Watching): reads typed characters into the matcher and, when a
/// trigger completes, hands the expansion to a worker. It keeps no key from the app, except the space, Tab or Enter that
/// completes an After-delimiter trigger: that key's text goes in after the snippet, and Tab or Enter would otherwise move
/// focus or send the message with the trigger in it. Memory only; nothing typed is logged.
/// </summary>
internal sealed class SnippetFilter(IInputHub hub, SnippetMatcher matcher, Action<SnippetMatch, Delimiter?, long> expand, IKeyReader reader) : IInputFilter
{
    private nint _foreground;
    private uint _swallowed;
    private volatile string[] _ignoredApps = [];

    public InputOrder Order => InputOrder.Snippets;

    public InputDevices Devices => InputDevices.Both;

    public bool Pauses => true;

    public void SetIgnoredApps(IEnumerable<string> apps) =>
        _ignoredApps = [.. apps.Select(a => Path.GetFileName(a)).Where(n => !string.IsNullOrEmpty(n))];

    public bool Mouse(in MouseInput mouse)
    {
        if (mouse.Kind == MouseKind.Down)
            matcher.Reset();
        return false;
    }

    public void InputReset(InputDevices devices)
    {
        matcher.Reset();
        _swallowed = 0;
    }

    public bool Key(in KeyInput key)
    {
        uint vk = key.Vk & 0xFF;
        if (!key.Down)
        {
            if (vk == _swallowed)
                _swallowed = 0;
            return false;
        }
        if (key.WasDown && vk == _swallowed)
            return true; // the repeat of a delimiter that was kept
        switch (vk)
        {
            case 0x10 or 0xA0 or 0xA1 or 0x11 or 0xA2 or 0xA3 or 0x12 or 0xA4 or 0xA5 or 0x14 or 0x90 or 0x91:
                return false; // a modifier alone: the key after it decides
            case 0x5B or 0x5C or 0xE5: // Win, or an IME composing
                matcher.Reset();
                return false;
        }
        nint front = reader.Foreground();
        if (front != _foreground)
        {
            _foreground = front;
            matcher.Reset();
        }
        if (hub.ForegroundElevated || IsIgnored(hub.ForegroundApp))
        {
            matcher.Reset();
            return false;
        }
        if (reader.CommandHeld())
        {
            matcher.Reset();
            return false;
        }
        switch (vk)
        {
            case 0x08:
                matcher.Backspace();
                return false;
            case 0x20 or 0x09 or 0x0D:
                if (matcher.Delimiter() is not { } done)
                    return false;
                _swallowed = vk;
                expand(done, vk == 0x09 ? Delimiter.Tab : vk == 0x0D ? Delimiter.Enter : Delimiter.Space, key.Timestamp);
                return true;
            case >= 0x21 and <= 0x28 or 0x1B or 0x2D or 0x2E: // page keys, End, Home, arrows, Esc, Insert, Delete
                matcher.Reset();
                return false;
        }
        char c = reader.CharOf(vk, key.Scan);
        if (c == '\0')
            return false; // a dead key or a key that types nothing: the buffer keeps what it has
        if (matcher.Type(c) is { } match)
            expand(match, null, key.Timestamp);
        return false;
    }

    private bool IsIgnored(string? app)
    {
        var ignored = _ignoredApps;
        if (ignored.Length == 0 || string.IsNullOrEmpty(app))
            return false;
        string name = Path.GetFileName(app);
        foreach (var item in ignored)
            if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
