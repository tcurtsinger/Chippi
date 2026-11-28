// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Features.MacDesktop.Dock;

namespace Chippi.Features.MacDesktop.Tests;

/// <summary>The red badges on the dock's icons, read from the hidden taskbar's buttons (owner's PC, 2026-11-26).</summary>
public sealed class DockBadgeTests
{
    [Theory]
    [InlineData("9 notifications", "9")]
    [InlineData("Attention requested, UnReaded", "")]   // Zoom: flashing, and a badge with no number
    [InlineData("Attention requested", "")]             // flashing alone
    [InlineData("1 unread message", "1")]
    [InlineData("150 notifications", "99+")]
    [InlineData("Attention requested, 3 new", "3")]
    [InlineData("0 notifications", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void The_help_text_becomes_a_badge(string? help, string? badge) =>
        Assert.Equal(badge, DockBadges.Read(help));

    /// <summary>Outlook's button has its window's title as help text: that isn't a badge.</summary>
    [Fact]
    public void A_window_title_is_not_a_badge() =>
        Assert.Null(DockBadges.Read("Mail - Someone - Outlook", ["Mail - Someone - Outlook"]));

    [Theory]
    [InlineData("Discord - 1 running window pinned", "Discord")]
    [InlineData("Zoom Workplace - 1 running window", "Zoom Workplace")]
    [InlineData("File Explorer - 2 running windows pinned", "File Explorer")]
    [InlineData("File Explorer pinned", "File Explorer")]
    [InlineData("Notepad", "Notepad")]
    public void A_button_is_named_for_its_app(string name, string app) =>
        Assert.Equal(app, DockBadges.AppName(name));

    private static ListedWindow Window(nint handle, string exe, string title = "Title") => new(handle, 1, "App", title, exe, false, 0);

    private static readonly TaskbarButton[] Buttons =
    [
        new("Discord - 1 running window pinned", "com.squirrel.Discord.Discord", "9 notifications"),
        new("Zoom Workplace - 1 running window", "zoom.us.Zoom Video Meetings", "Attention requested, UnReaded"),
        new("qBittorrent - 1 running window", @"{6D809377-6AF0-444B-8957-A3773F02200E}\qBittorrent\qbittorrent.exe", "2 done"),
        new("Steam - 1 running window", "Valve.Steam.Client", ""),
    ];

    private static string? Folder(Guid id) => id == new Guid("6D809377-6AF0-444B-8957-A3773F02200E") ? @"C:\Program Files" : null;

    [Fact]
    public void A_button_is_found_by_the_windows_app_id()
    {
        var window = Window(1, @"C:\Users\me\AppData\Local\Discord\app-1.0.9\Discord.exe");
        var app = new DockApp("D", "d", window.Executable, "Something else", true, [window]);
        Assert.Equal("9 notifications", DockBadges.Find(app, Buttons, _ => "com.squirrel.Discord.Discord", Folder)?.Help);
    }

    [Fact]
    public void A_button_is_found_by_its_program_in_a_known_folder()
    {
        var window = Window(2, @"C:\Program Files\qBittorrent\qbittorrent.exe");
        var app = new DockApp("Q", "q", window.Executable, "qBit", false, [window]);
        Assert.Equal("2 done", FindIn(app)?.Help);
    }

    [Fact]
    public void A_button_is_found_by_its_name_last()
    {
        var window = Window(3, @"C:\Program Files\Zoom\bin\Zoom.exe");
        var app = new DockApp("Z", "z", window.Executable, "Zoom Workplace", true, [window]);
        Assert.Equal("Attention requested, UnReaded", FindIn(app)?.Help);
    }

    [Fact]
    public void An_app_that_is_not_running_has_no_badge()
    {
        var app = new DockApp("D", "d", @"C:\Discord.exe", "Discord", true, []);
        Assert.Null(FindIn(app));
    }

    [Fact]
    public void A_path_id_is_written_out_and_other_ids_are_not_paths()
    {
        Assert.Equal(@"C:\Program Files\qBittorrent\qbittorrent.exe", DockBadges.Program(@"{6D809377-6AF0-444B-8957-A3773F02200E}\qBittorrent\qbittorrent.exe", Folder));
        Assert.Equal(@"X:\code\tool.exe", DockBadges.Program(@"X:\code\tool.exe"));
        Assert.Null(DockBadges.Program("com.squirrel.Discord.Discord"));
        Assert.Null(DockBadges.Program(@"{F38BF404-1D43-42F2-9305-67DE0B28FC23}\notepad.exe", Folder)); // a folder it can't resolve
    }

    private static TaskbarButton? FindIn(DockApp app) => DockBadges.Find(app, Buttons, _ => null, Folder);
}
