// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Features.Uninstaller;

namespace Chippi.Features.Uninstaller.Tests;

public sealed class LeftoverTests : IDisposable
{
    private readonly Fixture _f = new();
    public void Dispose() => _f.Dispose();
    internal Fixture Fx => _f;

    internal const string FirefoxName = "Mozilla Firefox (x64 en-US)", ThunderbirdName = "Mozilla Thunderbird (x64 en-US)";

    /// <summary>
    /// Firefox just uninstalled (so its entry is gone) with Thunderbird still installed: the Mozilla case of the brief. Both leave files in the same
    /// places; the shared Mozilla folder and key must never be Safe.
    /// </summary>
    internal AppRecord Mozilla(bool helperRecord = true)
    {
        string firefox = _f.Folder(_f.ProgramFiles("Mozilla Firefox"));
        _f.WriteFile(Path.Combine(firefox, "firefox.exe"), 4000);
        _f.WriteFile(Path.Combine(firefox, "omni.ja"), 9000);
        string thunderbird = _f.Folder(_f.ProgramFiles("Mozilla Thunderbird"));
        _f.WriteFile(Path.Combine(thunderbird, "thunderbird.exe"), 5000);
        _f.Registry.Add(AppSource.Machine64, "Mozilla Thunderbird 128.4.3 (x64 en-US)", ThunderbirdName, "Mozilla", "128.4.3", thunderbird, "\"" + Path.Combine(thunderbird, "uninstall", "helper.exe") + "\"");

        _f.WriteFile(Path.Combine(_f.Roaming("Mozilla", "Firefox"), "profiles.ini"), 500);
        _f.WriteFile(Path.Combine(_f.Roaming("Mozilla", "Thunderbird"), "profiles.ini"), 500);
        _f.WriteFile(Path.Combine(_f.Local("Mozilla", "Firefox"), "cache.bin"), 2000);
        _f.WriteFile(Path.Combine(_f.Data("Mozilla", "updates"), "u.bin"), 300);

        string menu = _f.Roots.StartMenuPrograms[0];
        _f.Shortcuts.Targets[_f.WriteFile(Path.Combine(menu, "Firefox.lnk"), 1)] = Path.Combine(firefox, "firefox.exe");
        _f.Shortcuts.Targets[_f.WriteFile(Path.Combine(menu, "Thunderbird.lnk"), 1)] = Path.Combine(thunderbird, "thunderbird.exe");
        _f.Shortcuts.Targets[_f.WriteFile(Path.Combine(_f.Roots.Desktops[0], "Firefox.lnk"), 1)] = Path.Combine(firefox, "firefox.exe");

        // The helper removes a machine key only when the record names it (Software\<publisher>\<name>); the other is never offered.
        _f.Keys.Add(RegHive.Machine64, @"Software\Mozilla\Mozilla Firefox (x64 en-US)\132.0.1");
        _f.Keys.Add(RegHive.Machine64, @"Software\Mozilla\Firefox\132.0.1 (x64 en-US)");
        _f.Keys.Add(RegHive.Machine64, @"Software\Mozilla\Thunderbird\128.4.3");
        _f.Keys.Add(RegHive.User, @"Software\Mozilla\Firefox");
        _f.Keys.Add(RegHive.User, @"Software\Mozilla\Thunderbird");
        _f.Keys.Add(RegHive.Machine64, @"Software\Microsoft\Windows\CurrentVersion\Run");

        return new AppRecord(Guid.NewGuid(), FirefoxName, "Mozilla", "132.0.1", AppSource.Machine64, "Mozilla Firefox 132.0.1 (x64 en-US)", firefox, null, [],
            null, _f.Now.UtcDateTime, helperRecord ? Guid.NewGuid() : null);
    }

    internal async Task<LeftoverScan> ScanAsync(AppRecord record)
    {
        using var engine = _f.Engine();
        return (await engine.ScanAsync(record))!;
    }

    private static LeftoverItem Find(LeftoverScan scan, string path) => scan.Items.Single(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task Firefox_with_Thunderbird_installed_puts_the_shared_Mozilla_folder_and_key_in_Shared_never_Safe()
    {
        var scan = await ScanAsync(Mozilla());
        Assert.True(scan.HasRecord);

        var install = Find(scan, _f.ProgramFiles("Mozilla Firefox"));
        Assert.Equal(LeftoverClass.Safe, install.Class);
        Assert.True(install.NeedsAdmin);
        Assert.True(install.TickedByDefault);
        Assert.Equal(13000, install.Bytes);

        var sharedFolder = Find(scan, _f.Data("Mozilla"));
        Assert.Equal(LeftoverClass.Shared, sharedFolder.Class);
        Assert.Equal(ThunderbirdName, sharedFolder.SharedWith);
        Assert.False(sharedFolder.CanTick);
        Assert.False(sharedFolder.TickedByDefault);
        var sharedKey = Find(scan, @"HKLM\Software\Mozilla");
        Assert.Equal((LeftoverClass.Shared, ThunderbirdName), (sharedKey.Class, sharedKey.SharedWith));

        Assert.All(scan.Shared, i => Assert.NotEqual(LeftoverClass.Safe, i.Class));
        Assert.DoesNotContain(scan.Items, i => i.Path.Contains("Thunderbird", StringComparison.OrdinalIgnoreCase));   // Thunderbird's own folders and keys are never listed
    }

    [Fact]
    public async Task A_folder_named_like_the_app_is_Check_first_and_a_shortcut_into_the_install_folder_is_Safe()
    {
        var scan = await ScanAsync(Mozilla());
        Assert.Equal(new[] { "HKCU\\Software\\Mozilla\\Firefox", "HKLM\\Software\\Mozilla\\Mozilla Firefox (x64 en-US)", _f.Local("Mozilla", "Firefox"), _f.Roaming("Mozilla", "Firefox") }
            .Order(StringComparer.OrdinalIgnoreCase), scan.CheckFirst.Select(i => i.Path).Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(scan.CheckFirst, i => Assert.False(i.TickedByDefault));
        Assert.Equal(new[] { Path.Combine(_f.Roots.Desktops[0], "Firefox.lnk"), Path.Combine(_f.Roots.StartMenuPrograms[0], "Firefox.lnk"), _f.ProgramFiles("Mozilla Firefox") }
            .Order(StringComparer.OrdinalIgnoreCase), scan.Safe.Select(i => i.Path).Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(scan.Safe, i => Assert.True(i.TickedByDefault));
        Assert.True(Find(scan, @"HKLM\Software\Mozilla\Mozilla Firefox (x64 en-US)").NeedsAdmin);
        Assert.DoesNotContain(scan.Items, i => i.Path.Equals(@"HKLM\Software\Mozilla\Firefox", StringComparison.OrdinalIgnoreCase));
        Assert.False(Find(scan, @"HKCU\Software\Mozilla\Firefox").NeedsAdmin);
    }

    [Fact]
    public async Task With_no_record_only_Check_first_items_are_offered()
    {
        var scan = await ScanAsync(Mozilla(helperRecord: false));
        Assert.False(scan.HasRecord);
        Assert.Empty(scan.Safe);
        Assert.NotEmpty(scan.CheckFirst);
        Assert.Equal(LeftoverClass.CheckFirst, Find(scan, _f.ProgramFiles("Mozilla Firefox")).Class);
        Assert.Equal(LeftoverClass.Shared, Find(scan, _f.Data("Mozilla")).Class);   // shared stays shared
    }

    [Fact]
    public async Task A_folder_another_app_sits_inside_or_contains_is_Shared()
    {
        var record = Mozilla();
        // Another app installed inside Firefox's own folder: Firefox's folder holds it, so it is not Firefox's to take.
        string inner = _f.Folder(Path.Combine(record.InstallLocation!, "plugin-host"));
        _f.Registry.Add(AppSource.User, "Plugin", "Plugin Host", "Other", "1.0", inner, "\"x.exe\"");
        var scan = await ScanAsync(record);
        var item = Find(scan, record.InstallLocation!);
        Assert.Equal((LeftoverClass.Shared, "Plugin Host"), (item.Class, item.SharedWith));
    }

    [Fact]
    public async Task Another_apps_uninstaller_or_icon_folder_counts_as_its_folder()
    {
        var record = Mozilla();
        string common = _f.Folder(_f.ProgramFiles("Shared Stuff"));
        string sharedFolder = _f.Roaming("Mozilla", "Firefox");
        _f.Registry.Add(AppSource.User, "Other", "Other App", "Someone", "1.0", null, "\"" + Path.Combine(sharedFolder, "other-uninstall.exe") + "\"");
        var scan = await ScanAsync(record);
        Assert.Equal("Other App", Find(scan, sharedFolder).SharedWith);
        Assert.NotNull(common);
    }

    [Fact]
    public async Task Nothing_under_Windows_and_no_container_is_ever_listed()
    {
        string system32 = _f.Folder(_f.Windows("System32", "Foo"));
        foreach (string location in new[] { system32, _f.Roots.ProgramFiles[0], _f.Roots.AppData, _f.Roots.UserProfile, _f.Roots.ProgramData, _f.Windows() })
        {
            var record = new AppRecord(Guid.NewGuid(), "Foo Tool", "Foo", "1", AppSource.Machine64, "Foo", location, null, [system32], null, _f.Now.UtcDateTime, Guid.NewGuid());
            var scan = await ScanAsync(record);
            Assert.DoesNotContain(scan.Items, i => PathRules.Contains(_f.Roots.WindowsDirectory, i.Path));
            Assert.DoesNotContain(scan.Items, i => i.Path == location);
        }
    }

    [Fact]
    public async Task No_Microsoft_key_and_no_Chippi_folder_is_ever_listed()
    {
        _f.Keys.Add(RegHive.Machine64, @"Software\Microsoft\Contoso Tool");
        _f.Keys.Add(RegHive.User, @"Software\Microsoft");
        _f.Folder(Path.Combine(_f.Roots.ProtectedTrees[0], "Contoso Tool"));
        var record = new AppRecord(Guid.NewGuid(), "Contoso Tool", "Microsoft Corporation", "1", AppSource.Machine64, "Contoso", null, null, [], null, _f.Now.UtcDateTime, Guid.NewGuid());
        var scan = await ScanAsync(record);
        Assert.DoesNotContain(scan.Items, i => i.Path.Contains("Microsoft", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(scan.Items, i => i.Path.Contains("Chippi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_file_held_open_is_In_use_and_a_folder_too_big_for_the_bin_is_tagged()
    {
        var record = Mozilla();
        using var held = new FileStream(Path.Combine(record.InstallLocation!, "firefox.exe"), FileMode.Open, FileAccess.Read, FileShare.None);
        _f.Recycler.TooBig.Add(_f.Roaming("Mozilla", "Firefox"));
        var scan = await ScanAsync(record);
        var install = Find(scan, record.InstallLocation!);
        Assert.True(install.InUse);
        Assert.False(install.TickedByDefault);   // in use: not ticked
        var big = Find(scan, _f.Roaming("Mozilla", "Firefox"));
        Assert.True(big.NoRecycleBin);
        Assert.False(big.InUse);
    }

    [Fact]
    public async Task A_link_is_listed_as_a_link_and_its_target_is_never_measured_or_followed()
    {
        var record = Mozilla();
        string keep = _f.WriteFile(_f.Outside("precious", "keep.bin"), 50_000);
        string link = _f.Junction(_f.Roaming("Firefox"), _f.Outside("precious"));
        _f.Junction(Path.Combine(record.InstallLocation!, "profile-link"), _f.Outside("precious"));
        var scan = await ScanAsync(record);
        var item = Find(scan, link);
        Assert.True(item.IsLink);
        Assert.Null(item.Bytes);
        Assert.Equal(13000, Find(scan, record.InstallLocation!).Bytes);   // the junction inside the install folder added nothing
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public void Names_are_compared_whole_so_App_is_not_AppData()
    {
        Assert.False(PathRules.Contains(@"C:\Users\Travis\App", @"C:\Users\Travis\AppData\Local"));
        Assert.True(PathRules.Contains(@"C:\Users\Travis\AppData", @"C:\Users\Travis\AppData\Local"));
        Assert.True(PathRules.Contains(@"C:\Users\Travis\AppData\", @"c:\users\travis\appdata"));
        Assert.Equal("mozilla", LeftoverScanner.ShortKey("Mozilla Corporation"));
        Assert.Equal("Python", LeftoverScanner.StripVersion("Python 3.13.0 (64-bit)"));
        Assert.Equal("Mozilla Firefox", LeftoverScanner.StripVersion("Mozilla Firefox (x64 en-US)"));
    }

    [Fact]
    public async Task A_machine_key_the_record_does_not_name_is_never_offered_and_one_it_names_is()
    {
        var record = Mozilla();
        _f.Keys.Add(RegHive.Machine64, @"Software\Mozilla Firefox (x64 en-US)");
        _f.Keys.Add(RegHive.Machine32, @"Software\Mozilla\Firefox");
        var scan = await ScanAsync(record);
        Assert.Equal(LeftoverClass.CheckFirst, Find(scan, @"HKLM\Software\Mozilla Firefox (x64 en-US)").Class);
        Assert.Equal(LeftoverClass.CheckFirst, Find(scan, @"HKLM\Software\Mozilla\Mozilla Firefox (x64 en-US)").Class);
        Assert.DoesNotContain(scan.Items, i => i.Kind == LeftoverKind.Key && i.Hive != RegHive.User && i.Path.EndsWith(@"\Mozilla\Firefox", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(LeftoverClass.CheckFirst, Find(scan, @"HKCU\Software\Mozilla\Firefox").Class);   // the owner's own key stays: Chippi removes it
    }

    [Fact]
    public async Task An_all_users_shortcut_goes_to_the_helper_as_Check_first_and_the_owners_own_stays_Safe()
    {
        var record = Mozilla();
        string common = _f.WriteFile(Path.Combine(_f.Roots.StartMenuPrograms[1], "Firefox.lnk"), 1);
        _f.Shortcuts.Targets[common] = Path.Combine(record.InstallLocation!, "firefox.exe");
        var scan = await ScanAsync(record);
        var all = Find(scan, common);
        Assert.Equal((LeftoverClass.CheckFirst, true, false), (all.Class, all.NeedsAdmin, all.TickedByDefault));
        var own = Find(scan, Path.Combine(_f.Roots.StartMenuPrograms[0], "Firefox.lnk"));
        Assert.Equal((LeftoverClass.Safe, false), (own.Class, own.NeedsAdmin));
    }

    [Fact]
    public void The_machine_key_rule_matches_the_helpers()
    {
        var record = new AppRecord(Guid.NewGuid(), "Fixture Notes", "Fixture Labs", "1.0", AppSource.Machine64, "K", null, null, [], null, DateTime.UtcNow, Guid.NewGuid());
        Assert.True(LeftoverScanner.NamedByRecord(record, @"Software\Fixture Notes"));
        Assert.True(LeftoverScanner.NamedByRecord(record, @"Software\Fixture Labs\Fixture Notes\Window"));
        Assert.False(LeftoverScanner.NamedByRecord(record, @"Software\Fixture Labs\Notes"));
        Assert.False(LeftoverScanner.NamedByRecord(record, @"Software\Fixture Labs"));
        Assert.False(LeftoverScanner.NamedByRecord(record, @"Software\Fixture NotesExtra"));
        Assert.False(LeftoverScanner.NamedByRecord(record with { Name = @"A\B" }, @"Software\A\B"));
    }
}
