// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using static Chippi.Audio.Tests.FakeDeviceSystem;

namespace Chippi.Audio.Tests;

public class SessionCatalogTests
{
    internal static AudioSessionInfo Session(string id, string endpoint, int pid, string? exe, string name,
        SessionState state = SessionState.Active, float volume = 1f, bool muted = false, bool system = false) =>
        new(id, endpoint, pid, exe, name, system, state, volume, muted);

    [Fact]
    public void Sessions_of_one_exe_group_into_one_app_across_processes_and_outputs()
    {
        var apps = SessionCatalog.Group([
            Session("1", "Spk", 10, @"C:\Apps\Chat.exe", "Chat", SessionState.Inactive, volume: 0.2f),
            Session("2", "Hdp", 11, @"c:\apps\chat.EXE", "Chat", SessionState.Active, volume: 0.7f),
            Session("3", "Spk", 12, @"C:\Apps\Game.exe", "Game"),
        ]);

        Assert.Equal(2, apps.Count);
        var chat = apps[0];
        Assert.Equal(AppKey.ForExe(@"C:\Apps\Chat.exe"), chat.Key);
        Assert.Equal(0.7f, chat.Volume); // the active session leads
        Assert.True(chat.IsActive);
        Assert.Equal(["Hdp", "Spk"], chat.EndpointIds); // where it's heard first
    }

    [Fact]
    public void Without_an_exe_path_the_display_name_is_the_identity_and_system_sounds_have_their_own()
    {
        var apps = SessionCatalog.Group([
            Session("1", "Spk", 0, null, "@%SystemRoot%\\System32\\AudioSrv.Dll,-202", system: true),
            Session("2", "Spk", 20, null, "Protected app"),
        ]);

        Assert.Equal(AppKey.SystemSounds, apps[0].Key);
        Assert.Equal("System sounds", apps[0].Name);
        Assert.Equal(AppKey.ForName("Protected app"), apps[1].Key);
    }

    [Fact]
    public void A_squirrel_app_keeps_its_key_across_updates()
    {
        Assert.Equal(
            AppKey.ForExe(@"C:\Users\T\AppData\Local\Discord\app-1.0.9265\Discord.exe"),
            AppKey.ForExe(@"C:\Users\T\AppData\Local\Discord\app-1.0.9301\Discord.exe"));
        Assert.NotEqual(AppKey.ForExe(@"C:\a\app-1\x.exe"), AppKey.ForExe(@"C:\b\app-1\x.exe"));
    }

    [Fact]
    public void An_app_is_muted_only_when_every_session_is()
    {
        var apps = SessionCatalog.Group([
            Session("1", "Spk", 10, @"C:\a.exe", "A", muted: true),
            Session("2", "Spk", 11, @"C:\a.exe", "A", muted: false),
        ]);
        Assert.False(apps[0].Muted);
    }

    [Fact]
    public void Expired_sessions_and_chippis_own_are_left_out()
    {
        var sys = new FakeDeviceSystem().With(Speaker("Spk"))
            .WithSession(Session("1", "Spk", 10, @"C:\a.exe", "A", SessionState.Expired))
            .WithSession(Session("2", "Spk", 99, @"C:\Chippi.exe", "Chippi"))
            .WithSession(Session("3", "Spk", 11, @"C:\b.exe", "B"));
        var catalog = new SessionCatalog(ownProcessId: 99);

        catalog.Refresh(sys, sys.GetEndpoints(AudioFlow.Render));

        Assert.Equal("B", Assert.Single(catalog.Apps).Name);
    }
}
