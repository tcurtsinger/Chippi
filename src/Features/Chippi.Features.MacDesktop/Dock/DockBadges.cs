// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Chippi.Core.Actions;

namespace Chippi.Features.MacDesktop.Dock;

/// <summary>
/// One app button on the hidden taskbar as UI Automation describes it: its name ("Discord - 1 running window pinned"), the
/// AppUserModelID from its automation ID ("Appid: com.squirrel.Discord.Discord") and its help text, where Windows puts the
/// description an app gives its overlay icon ("9 notifications") and "Attention requested" while the button flashes.
/// </summary>
public sealed record TaskbarButton(string Name, string AppId, string Help);

/// <summary>
/// The red badges on the dock's icons (docs/mac-style/brief.md, Dock): read from what the hidden taskbar says about its
/// buttons, because only Explorer knows an app's overlay icon and there's no public way to ask it.
/// </summary>
public static partial class DockBadges
{
    /// <summary>What Windows puts first in a flashing button's help text.</summary>
    public const string Attention = "Attention requested";

    /// <summary>The prefix of a taskbar button's automation ID.</summary>
    public const string AppIdPrefix = "Appid: ";

    /// <summary>
    /// The badge a button's help text asks for: null for none, "" for a plain dot (a flashing button, or a badge with no
    /// number, as Zoom's "UnReaded"), else the count, "99+" past 99. Text that is only one of the app's window titles is
    /// the button's tooltip, not a badge (Outlook's).
    /// </summary>
    public static string? Read(string? help, IEnumerable<string>? titles = null)
    {
        string text = (help ?? "").Trim();
        bool attention = false;
        if (text.StartsWith(Attention, StringComparison.OrdinalIgnoreCase))
        {
            attention = true;
            text = text[Attention.Length..].TrimStart(',', ';', ' ', '.');
        }
        if (text.Length > 0 && titles is not null && titles.Any(t => string.Equals(t.Trim(), text, StringComparison.OrdinalIgnoreCase)))
            text = "";
        if (text.Length == 0)
            return attention ? "" : null;
        var number = Number().Match(text);
        if (!number.Success)
            return "";
        if (!int.TryParse(number.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count > 99)
            return "99+";
        // "0 notifications" is an app saying it has none, unless the button flashes as well.
        return count > 0 ? count.ToString(CultureInfo.InvariantCulture) : attention ? "" : null;
    }

    /// <summary>
    /// The taskbar button for a running app: the one with a window's AppUserModelID, else the one whose ID is the program's
    /// path (Windows writes a desktop app's ID that way, its folder as a known-folder GUID), else the one with its name.
    /// </summary>
    public static TaskbarButton? Find(DockApp app, IReadOnlyList<TaskbarButton> buttons, Func<ListedWindow, string?> appIdOf,
        Func<Guid, string?>? knownFolder = null)
    {
        if (!app.Running || buttons.Count == 0)
            return null;
        var ids = app.Windows.Select(appIdOf).OfType<string>().Where(id => id.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (buttons.FirstOrDefault(b => ids.Contains(b.AppId)) is { } byId)
            return byId;
        var programs = app.Windows.Select(w => w.Executable).Append(app.Path).Where(p => p.Length > 0).ToArray();
        foreach (var button in buttons)
            if (Program(button.AppId, knownFolder) is { } program && programs.Any(p => DockApps.SameProgram(program, p) || DockApps.SameProgram(p, program)))
                return button;
        return buttons.FirstOrDefault(b => string.Equals(AppName(b.Name), app.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The program an automation ID names as a path, its known folder written out; null when it isn't a path.</summary>
    public static string? Program(string appId, Func<Guid, string?>? knownFolder = null)
    {
        var folder = KnownFolderPrefix().Match(appId);
        if (folder.Success)
        {
            if (knownFolder?.Invoke(Guid.Parse(folder.Groups[1].Value)) is not { Length: > 0 } root)
                return null;
            return Path.Join(root, appId[folder.Length..]);
        }
        return appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathFullyQualified(appId) ? appId : null;
    }

    /// <summary>A button's name without what Windows adds to it: " - 2 running windows" and " pinned".</summary>
    public static string AppName(string name)
    {
        string text = name.Trim();
        if (text.EndsWith(" pinned", StringComparison.OrdinalIgnoreCase))
            text = text[..^" pinned".Length];
        var running = Running().Match(text);
        return running.Success ? text[..running.Index] : text;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();

    [GeneratedRegex(@"^\{([0-9A-Fa-f-]{36})\}\\?")]
    private static partial Regex KnownFolderPrefix();

    [GeneratedRegex(@" - \d+ running windows?$", RegexOptions.IgnoreCase)]
    private static partial Regex Running();
}
