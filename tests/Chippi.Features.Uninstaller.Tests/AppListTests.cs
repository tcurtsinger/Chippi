// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Features.Uninstaller;

namespace Chippi.Features.Uninstaller.Tests;

public sealed class AppListTests : IDisposable
{
    private readonly Fixture _f = new();
    public void Dispose() => _f.Dispose();

    private AppReader Reader() => new(_f.Registry, _f.Packages, _ => false);

    private static RawPackage Package(string name, string display, string publisher = "Contoso", string version = "1.0.0.0", bool framework = false, bool resource = false, bool system = false) =>
        new("Full_" + name, "Family_" + name, name, display, publisher, version, @"C:\WindowsApps\" + name, new DateTime(2026, 11, 15), framework, resource, system);

    [Fact]
    public void The_list_reads_both_machine_views_the_users_own_and_the_Store()
    {
        _f.Registry.Add(AppSource.Machine64, "Git_is1", "Git", "The Git Development Community", "2.47.0", uninstall: @"""C:\Program Files\Git\unins000.exe""");
        _f.Registry.Add(AppSource.Machine32, "WinRAR", "WinRAR", "win.rar GmbH", "7.01", uninstall: @"""C:\Program Files (x86)\WinRAR\uninstall.exe""");
        _f.Registry.Add(AppSource.User, "Discord", "Discord", "Discord Inc.", "1.0.9171", uninstall: @"""C:\Users\Travis\AppData\Local\Discord\Update.exe"" --uninstall");
        _f.Packages.Packages.Add(Package("Calc", "Calculator", "Microsoft Corporation"));
        var apps = Reader().Read();
        Assert.Equal(["Calculator", "Discord", "Git", "WinRAR"], apps.Select(a => a.Name));
        Assert.Equal([AppSource.Store, AppSource.User, AppSource.Machine64, AppSource.Machine32], apps.Select(a => a.Source));
        Assert.All(apps, a => Assert.True(a.CanUninstall));
        Assert.Equal([false, false, true, true], apps.Select(a => a.NeedsAdmin));
    }

    [Fact]
    public void System_components_and_updates_are_skipped_as_the_entries_ask()
    {
        _f.Registry.Add(AppSource.Machine64, "A", "Hidden thing", extra: [("SystemComponent", 1)]);
        _f.Registry.Add(AppSource.Machine64, "B", "Update for Foo (KB5001234)");
        _f.Registry.Add(AppSource.Machine64, "C", "Foo 2 patch", extra: [("ParentKeyName", "Foo")]);
        _f.Registry.Add(AppSource.Machine64, "D", "Foo hotfix", extra: [("ReleaseType", "Hotfix")]);
        _f.Registry.Add(AppSource.Machine64, "E", "");
        _f.Registry.Add(AppSource.Machine64, "F", "Real app", uninstall: @"""C:\x\u.exe""");
        Assert.Equal(["Real app"], Reader().Read().Select(a => a.Name));
    }

    [Fact]
    public void The_same_app_registered_twice_or_in_the_Store_is_one_row()
    {
        _f.Registry.Add(AppSource.Machine64, "K1", "Notepad++", "Notepad++ Team", "8.7.1", uninstall: @"""C:\a\u.exe""");
        _f.Registry.Add(AppSource.Machine32, "K2", "Notepad++", "Notepad++ Team", "8.7.1", uninstall: @"""C:\b\u.exe""");
        _f.Registry.Add(AppSource.User, "K3", "Notepad++", "Notepad++ Team", "8.7.2", uninstall: @"""C:\c\u.exe""");   // another version: another row
        _f.Packages.Packages.Add(Package("Tool", "Notepad++", "Notepad++ Team", "8.7.1"));
        var apps = Reader().Read();
        Assert.Equal(2, apps.Count);
        Assert.Equal([AppSource.Machine64, AppSource.User], apps.Select(a => a.Source));   // the machine key won over its copy and the package
    }

    [Fact]
    public void Components_Chippi_and_apps_without_a_command_get_no_verb()
    {
        _f.Registry.Add(AppSource.Machine64, "dn", "Microsoft .NET Runtime 8.0.10", "Microsoft Corporation", uninstall: @"""C:\x\u.exe""");
        _f.Registry.Add(AppSource.Machine64, "wv", "Microsoft Edge WebView2 Runtime", "Microsoft Corporation", uninstall: @"""C:\x\u.exe""");
        _f.Registry.Add(AppSource.User, "Chippi", "Chippi", "Chippi", uninstall: @"""C:\x\u.exe""");
        _f.Registry.Add(AppSource.Machine64, "nc", "No command app", "Acme");
        _f.Registry.Add(AppSource.Machine64, "nr", "Pinned app", "Acme", uninstall: @"""C:\x\u.exe""", extra: [("NoRemove", 1)]);
        _f.Packages.Packages.Add(Package("fx", "VCLibs", "Microsoft Corporation", framework: true));
        _f.Packages.Packages.Add(Package("res", "Language pack", resource: true));
        _f.Packages.Packages.Add(Package("sys", "Photos", "Microsoft Corporation", system: true));
        var apps = Reader().Read().ToDictionary(a => a.Name);
        Assert.False(apps.ContainsKey("Language pack"));   // a resource package is not an app
        Assert.All(new[] { "Microsoft .NET Runtime 8.0.10", "Microsoft Edge WebView2 Runtime", "VCLibs", "Photos" }, n => { Assert.Equal(AppKind.Component, apps[n].Kind); Assert.False(apps[n].CanUninstall); });
        Assert.Equal(AppKind.Self, apps["Chippi"].Kind);
        Assert.False(apps["Chippi"].CanUninstall);
        Assert.False(apps["No command app"].CanUninstall);
        Assert.Equal("No uninstaller", apps["No command app"].NoVerbText);
        Assert.False(apps["Pinned app"].CanUninstall);
        Assert.Null(apps["Pinned app"].NoVerbText);
    }

    [Fact]
    public void Each_row_carries_its_columns_size_date_icon_and_the_recent_mark()
    {
        _f.Registry.Add(AppSource.Machine64, "{12345678-1234-1234-1234-123456789ABC}", "Firefox", "Mozilla", "132.0.1", @"C:\Program Files\Mozilla Firefox",
            extra: [("InstallDate", "20261115"), ("EstimatedSize", 236544), ("DisplayIcon", @"""C:\Program Files\Mozilla Firefox\firefox.exe"",0"), ("WindowsInstaller", 1)]);
        var app = Reader().Read().Single();
        Assert.Equal(236544L * 1024, app.SizeBytes);
        Assert.Equal(new DateTime(2026, 11, 15), app.Installed);
        Assert.Equal(@"C:\Program Files\Mozilla Firefox\firefox.exe", app.IconPath);
        Assert.True(app.IsMsi);
        Assert.True(app.CanUninstall);   // an MSI needs no command string
        Assert.True(app.IsRecent(new DateTime(2026, 11, 18)));
        Assert.False(app.IsRecent(new DateTime(2026, 11, 23)));
        Assert.Equal("231 MB", UninstallerText.Size(app.SizeBytes));
    }

    [Fact]
    public async Task Refresh_applies_the_read_saves_the_cache_and_a_new_engine_opens_with_it()
    {
        _f.Registry.Add(AppSource.User, "Slack", "Slack", "Slack Technologies", "4.40", uninstall: @"""C:\x\u.exe""");
        string cache = Path.Combine(_f.Base, "data", "apps.json");
        using var first = _f.Engine(cache: cache);
        Assert.False(first.Loaded);
        await first.RefreshAsync();
        Assert.True(first.Loaded);
        Assert.Equal(["Slack"], first.Apps.Select(a => a.Name));
        using var second = _f.Engine(cache: cache);
        await second.LoadCachedAsync();   // instant: no read yet
        Assert.Equal(["Slack"], second.Apps.Select(a => a.Name));
        Assert.True(second.Loaded);
    }

    [Fact]
    public void A_user_command_resolves_by_the_helpers_rules()
    {
        bool Exists(string p) => p is @"C:\Program Files\Foo Bar\unins.exe" or @"C:\Windows\System32\msiexec.exe";
        string Expand(string s) => s.Replace("%PF%", @"C:\Program Files");
        Assert.Equal(new UserCommand.Line(@"C:\Program Files\Foo Bar\unins.exe", "/S"), UserCommand.Parse(@"""C:\Program Files\Foo Bar\unins.exe"" /S", Expand, Exists));
        Assert.Equal(new UserCommand.Line(@"C:\Program Files\Foo Bar\unins.exe", "/VERYSILENT /x"), UserCommand.Parse(@"C:\Program Files\Foo Bar\unins.exe /VERYSILENT /x", Expand, Exists));
        Assert.Equal(new UserCommand.Line(@"C:\Program Files\Foo Bar\unins.exe", ""), UserCommand.Parse(@"%PF%\Foo Bar\unins.exe", Expand, Exists));
        Assert.Null(UserCommand.Parse(@"C:\Nowhere\unins.exe", Expand, Exists));
        Assert.Null(UserCommand.Parse(@"""C:\x\run.bat"" /S", Expand, Exists));
        Assert.Null(UserCommand.Parse("", Expand, Exists));
        var msi = new AppEntry("u:{12345678-1234-1234-1234-123456789ABC}", "X", "", "", null, null, null, AppSource.User, "{12345678-1234-1234-1234-123456789ABC}", null, AppKind.App)
        { IsMsi = true, ProductCode = "{12345678-1234-1234-1234-123456789ABC}", UninstallString = "MsiExec.exe /I{12345678-1234-1234-1234-123456789ABC}" };
        Assert.Equal(new UserCommand.Line(@"C:\Windows\System32\msiexec.exe", "/x {12345678-1234-1234-1234-123456789ABC} /passive /norestart"),
            UserCommand.For(msi, s => s.Replace("%SystemRoot%", @"C:\Windows"), Exists));
        var both = msi with { IsMsi = false, ProductCode = null, QuietUninstallString = @"""C:\Program Files\Foo Bar\unins.exe"" /S", UninstallString = @"""C:\Program Files\Foo Bar\unins.exe""" };
        Assert.Equal("/S", UserCommand.For(both, Expand, Exists)!.Arguments);   // the quiet string comes first
    }
}
