// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Notifications;

namespace Chippi.Core.Modules;

/// <summary>
/// Shell services the registry hands to each module through its <see cref="ModuleContext"/>. Built
/// once by the shell; the defaults suit tests and anything that never shows a notice.
/// </summary>
public sealed class ModuleServices
{
    /// <summary>%LOCALAPPDATA%\Chippi\data. Each module gets its own folder under it.</summary>
    public static string DefaultDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi", "data");

    /// <summary>Where module data folders live. Dev profiles pass their own, so they never share a database.</summary>
    public string DataRoot { get; init; } = DefaultDataRoot;

    public ActivationRouter Activation { get; init; } = new();

    /// <summary>Shows Windows notifications. Null until the shell's backend exists; notices then reach the island only.</summary>
    public IToastPresenter? Toasts { get; set; }

    /// <summary>How long a notice stays on the island before its activity is cleared.</summary>
    public TimeSpan IslandNoticeDuration { get; init; } = TimeSpan.FromSeconds(6);

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Opens Settings at a page id (<c>module:&lt;id&gt;/&lt;section&gt;</c>). Set by the shell; null in tests.</summary>
    public Action<string>? OpenSettings { get; set; }

    /// <summary>Opens the island on a tile key's page. Set by the shell; null in tests.</summary>
    public Action<string>? OpenIslandPage { get; set; }

    /// <summary>The shared clipboard service (Phase 5). Set by the shell; null in tests. Idle until a feature uses it.</summary>
    public Clipboard.IClipboardHub? Clipboard { get; set; }
}
