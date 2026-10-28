// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.Clipboard;

/// <summary>Settings › Clipboard history (design notes #52) and the page's Pause (notes #10).</summary>
public sealed record ClipboardHistorySettings
{
    /// <summary>Off until turned on, here or on the page (Travis, task 30).</summary>
    public bool Recording { get; init; }

    /// <summary>Set from the page; lasts until Resume, across restarts.</summary>
    public bool Paused { get; init; }

    /// <summary>Executable paths whose copies are never read.</summary>
    public List<string> AppsToSkip { get; init; } = [];

    public ClipboardHistorySettings Normalized() => this with
    {
        AppsToSkip = [.. (AppsToSkip ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase)],
    };
}

/// <summary>Settings › Clean URL (design notes #54).</summary>
public sealed record CleanUrlSettings
{
    /// <summary>Off by default.</summary>
    public bool Automatic { get; init; }

    /// <summary>Hosts, each covering its subdomains; automatic cleaning only (notes #27).</summary>
    public List<string> SitesToSkip { get; init; } = [];

    public CleanUrlSettings Normalized() => this with
    {
        SitesToSkip = [.. (SitesToSkip ?? []).Select(CleanUrl.Sites.Normalize).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)],
    };
}
