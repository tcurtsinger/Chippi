// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Shortcuts;

namespace Chippi.Features.Shortcuts;

/// <summary>The ways a binding is triggered: a registered key, a mouse button or a Caps combo.</summary>
public enum BindingDoor { Key, Mouse, Caps }

public enum CapsTap { Nothing, Capitals, Escape, InputLanguage }

/// <summary>The buttons and directions a binding may take (docs/phase-6/shortcuts.md). Left, right and wheel up and down are never offered.</summary>
public enum MouseTrigger { Middle, Back, Forward, WheelLeft, WheelRight }

/// <summary>One action on one door, as Settings › Shortcuts lists it.</summary>
public sealed record ShortcutBinding
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public BindingDoor Door { get; init; }

    public ChippiAction Action { get; init; } = new();

    /// <summary>A Key door's shortcut.</summary>
    public KeyShortcut? Keys { get; init; }

    /// <summary>A Mouse door's button or direction.</summary>
    public MouseTrigger Button { get; init; }

    public uint CapsKey { get; init; }

    /// <summary>What the action is called in a message: its name, else its kind.</summary>
    public string Label => string.IsNullOrWhiteSpace(Action.Name) ? Action.Kind.ToString() : Action.Name;

    /// <summary>The same door, whatever the action: two of these can't both be on the list.</summary>
    public bool SameDoor(ShortcutBinding other) =>
        Door == other.Door && (Door == BindingDoor.Key ? Keys is not null && Keys == other.Keys : Door == BindingDoor.Caps ? CapsKey == other.CapsKey : Button == other.Button);
}

/// <summary>What a row shows besides its door (docs/phase-6/shortcuts.md, States and clashes).</summary>
public enum BindingState
{
    Active,

    /// <summary>Its section's switch is off.</summary>
    Inactive,

    /// <summary>Another of Chippi's shortcuts holds the keys.</summary>
    UsedTwice,

    /// <summary><c>RegisterHotKey</c> failed: another app holds the keys.</summary>
    InUseByApp,

    /// <summary>The Radial menu opens from this button, so the shortcut waits.</summary>
    HeldByRadial,

    /// <summary>Windows reserves the combination. It saves and does nothing.</summary>
    InUseByWindows,

    /// <summary>Its target is gone, or there's nothing to run actions yet.</summary>
    Unavailable,
}

/// <param name="Text">What the row says for a state other than Active: "In use by Windows".</param>
public sealed record BindingStatus(BindingState State, string? Text = null)
{
    public bool Acts => State == BindingState.Active;
}

/// <param name="Error">Why it wasn't saved, as the page says it; null when it was.</param>
public sealed record BindingResult(string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>Settings › Shortcuts. The Mouse buttons section's switch starts off, even once installed (task 226).</summary>
public sealed record ShortcutsSettings
{
    public bool CapsEnabled { get; init; }
    public uint CapsHeldKey { get; init; } = 0x14;
    public KeyModifiers CapsModifiers { get; init; } = KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift;
    public CapsTap CapsTap { get; init; }
    public string[] CapsIgnoredApps { get; init; } = [];

    public bool MouseEnabled { get; init; }

    /// <summary>Exe names or paths whose windows keep their extra buttons: the press reaches the app.</summary>
    public string[] MouseIgnoredApps { get; init; } = [];

    public List<ShortcutBinding> Bindings { get; init; } = [];
}
