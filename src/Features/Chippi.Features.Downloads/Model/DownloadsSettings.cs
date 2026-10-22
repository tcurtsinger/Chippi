// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Rules;

namespace Chippi.Features.Downloads.Model;

/// <summary>How a watched folder treats new files (brief §2: only these three).</summary>
public enum FolderMode
{
    /// <summary>New files wait for review; nothing moves until approved. The default for a new folder.</summary>
    Manual,

    /// <summary>Ready files are organized automatically after the grace period.</summary>
    Silent,

    /// <summary>Not watched at all.</summary>
    Paused,
}

public sealed class WatchedFolder
{
    public string Path { get; set; } = "";

    public FolderMode Mode { get; set; } = FolderMode.Manual;

    /// <summary>Silent waits this long after the last write (brief §2: 30 s).</summary>
    public int GraceSeconds { get; set; } = 30;

    /// <summary>
    /// When the folder was added (or set to Silent). Files created before it were already there and
    /// are never moved silently; they show only in Organize now's preview (brief §2).
    /// </summary>
    public DateTimeOffset BaselineUtc { get; set; }
}

public enum RuleActionKind
{
    Move,
    Rename,
    Hold,
}

/// <summary>A rule as stored in settings.json. <see cref="ToRule"/> gives the Chippi.Rules record.</summary>
public sealed class RuleSetting
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public int Order { get; set; }

    public List<string>? Extensions { get; set; }

    public string? NameRegex { get; set; }

    public long? MinBytes { get; set; }

    public long? MaxBytes { get; set; }

    public DateOnly? ModifiedAfter { get; set; }

    public DateOnly? ModifiedBefore { get; set; }

    public RuleActionKind Action { get; set; }

    /// <summary>Destination folder (Move) or new name (Rename) template.</summary>
    public string? Template { get; set; }

    public string? HoldReason { get; set; }

    public RuleSetting Clone()
    {
        var copy = (RuleSetting)MemberwiseClone();
        copy.Extensions = Extensions is null ? null : [.. Extensions];
        return copy;
    }

    public Rule ToRule() => new(
        Id,
        Name,
        Enabled,
        Order,
        new Conditions
        {
            Extensions = Extensions,
            NameRegex = NameRegex,
            MinBytes = MinBytes,
            MaxBytes = MaxBytes,
            ModifiedAfter = ModifiedAfter,
            ModifiedBefore = ModifiedBefore,
        },
        Action switch
        {
            RuleActionKind.Rename => new RenameTo(Template ?? ""),
            RuleActionKind.Hold => new Hold(HoldReason ?? ""),
            _ => new MoveTo(Template ?? ""),
        });
}

/// <summary>The module's section of settings.json (<c>modules.downloads-organizer</c>).</summary>
public sealed class DownloadsSettings
{
    public List<WatchedFolder> Folders { get; set; } = [];

    public List<RuleSetting> Rules { get; set; } = [];

    /// <summary>History older than this is pruned at start (brief §2: 90 days).</summary>
    public int HistoryDays { get; set; } = 90;

    /// <summary>
    /// When the person last cleared history. History shows nothing older; the store keeps the few
    /// rows the self-trigger guard and partial-file cleanup still need.
    /// </summary>
    public DateTimeOffset? HistoryClearedUtc { get; set; }

    /// <summary>False until the first start has added the user's Downloads folder.</summary>
    public bool Initialized { get; set; }

    /// <summary>
    /// A deep copy to edit and pass to <c>DownloadsModule.SaveSettings</c>, which compares it with
    /// the current settings (a folder newly set to Silent gets a fresh baseline).
    /// </summary>
    public DownloadsSettings Clone() => new()
    {
        Folders = [.. Folders.Select(f => new WatchedFolder { Path = f.Path, Mode = f.Mode, GraceSeconds = f.GraceSeconds, BaselineUtc = f.BaselineUtc })],
        Rules = [.. Rules.Select(r => r.Clone())],
        HistoryDays = HistoryDays,
        HistoryClearedUtc = HistoryClearedUtc,
        Initialized = Initialized,
    };
}
