// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.Json.Nodes;
using Chippi.Core.Config;

namespace Chippi.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private sealed class SampleSettings
    {
        public int Minutes { get; set; }

        public bool KeepDisplayOn { get; set; }

        public List<string> Names { get; set; } = [];
    }

    [Fact]
    public void Round_trips_sections_and_module_settings()
    {
        var path = _dir.File("settings.json");
        var store = new ConfigStore(path);
        store.Load();
        store.SetSection(AppSettings.SectionName, new AppSettings { Theme = ThemePreference.Light, PetColor = PetColor.Ember, ReduceMotion = true });
        store.SetModuleSettings("keep-awake", new SampleSettings { Minutes = 90, KeepDisplayOn = true, Names = ["a", "b"] });

        var reloaded = new ConfigStore(path);
        reloaded.Load();

        var app = reloaded.GetSection<AppSettings>(AppSettings.SectionName);
        Assert.Equal(ThemePreference.Light, app.Theme);
        Assert.Equal(PetColor.Ember, app.PetColor);
        Assert.True(app.ReduceMotion);
        var module = reloaded.GetModuleSettings<SampleSettings>("keep-awake");
        Assert.Equal(90, module.Minutes);
        Assert.True(module.KeepDisplayOn);
        Assert.Equal(["a", "b"], module.Names);
    }

    [Fact]
    public void Writes_a_versioned_document_with_readable_enums()
    {
        var path = _dir.File("settings.json");
        var store = new ConfigStore(path);
        store.SetSection(AppSettings.SectionName, new AppSettings { Theme = ThemePreference.System });

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(ConfigStore.CurrentVersion, root["version"]!.GetValue<int>());
        Assert.Equal("system", root["app"]!["theme"]!.GetValue<string>());
    }

    [Fact]
    public void Missing_file_and_missing_section_give_defaults()
    {
        var store = new ConfigStore(_dir.File("nope.json"));
        store.Load();

        Assert.Equal(ThemePreference.Dark, store.GetSection<AppSettings>(AppSettings.SectionName).Theme);
        Assert.False(store.HasModuleSettings("keep-awake"));
        Assert.Equal(0, store.GetModuleSettings<SampleSettings>("keep-awake").Minutes);
    }

    [Fact]
    public void Save_replaces_atomically_and_leaves_no_temp_file()
    {
        var path = _dir.File("settings.json");
        var store = new ConfigStore(path);
        for (int i = 0; i < 20; i++)
            store.SetModuleSettings("m", new SampleSettings { Minutes = i });

        Assert.False(File.Exists(path + ".tmp"));
        Assert.Single(Directory.GetFiles(_dir.Path));
        var reloaded = new ConfigStore(path);
        reloaded.Load();
        Assert.Equal(19, reloaded.GetModuleSettings<SampleSettings>("m").Minutes);
    }

    [Fact]
    public void Interrupted_write_never_damages_the_saved_file()
    {
        var path = _dir.File("settings.json");
        var store = new ConfigStore(path);
        store.SetModuleSettings("m", new SampleSettings { Minutes = 1 });
        var before = File.ReadAllText(path);

        // Simulate a crash mid-write: a half-written temp file is left behind.
        File.WriteAllText(path + ".tmp", "{ \"version\": 1, \"modules\": { \"m\": { \"minu");
        Assert.Equal(before, File.ReadAllText(path));

        var reloaded = new ConfigStore(path);
        reloaded.Load();
        Assert.Equal(1, reloaded.GetModuleSettings<SampleSettings>("m").Minutes);

        // The next save overwrites the stale temp file and commits normally.
        reloaded.SetModuleSettings("m", new SampleSettings { Minutes = 2 });
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Contains("\"minutes\": 2", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readers_never_see_a_partial_file_while_saving()
    {
        var path = _dir.File("settings.json");
        var store = new ConfigStore(path);
        store.SetModuleSettings("m", new SampleSettings { Minutes = 0, Names = [.. Enumerable.Range(0, 200).Select(i => "name" + i)] });

        var writer = Task.Run(() =>
        {
            for (int i = 1; i <= 150; i++)
                store.SetModuleSettings("m", new SampleSettings { Minutes = i, Names = [.. Enumerable.Range(0, 200).Select(n => "name" + n)] });
        });

        int reads = 0;
        while (!writer.IsCompleted)
        {
            try
            {
                var text = File.ReadAllText(path);
                Assert.NotNull(JsonNode.Parse(text)); // throws on a torn file
                reads++;
                Thread.Sleep(1); // a real reader does not spin; a hot loop could starve the rename
            }
            catch (IOException)
            {
                // The rename can briefly lock the file; a lock is fine, a torn read is not.
            }
        }
        await writer;
        Assert.True(reads > 0);
    }

    [Fact]
    public void Corrupt_file_is_moved_aside_and_defaults_load()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, "{ this is not json");

        var store = new ConfigStore(path);
        store.Load();

        Assert.Equal(ThemePreference.Dark, store.GetSection<AppSettings>(AppSettings.SectionName).Theme);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_dir.Path, "settings.corrupt-*.json"));
    }

    [Fact]
    public void Unknown_top_level_sections_survive_a_save()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "futureFeature": { "enabled": true, "list": [1, 2, 3] },
              "app": { "theme": "light" }
            }
            """);

        var store = new ConfigStore(path);
        store.Load();
        store.SetSection(AppSettings.SectionName, new AppSettings { Theme = ThemePreference.Dark });

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(root["futureFeature"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(3, root["futureFeature"]!["list"]!.AsArray().Count);
        Assert.Equal("dark", root["app"]!["theme"]!.GetValue<string>());
    }

    [Fact]
    public void Unknown_properties_inside_a_known_section_survive_a_save()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "app": { "theme": "light", "addedInV2": "keep me", "nested": { "x": 1 } },
              "modules": { "keep-awake": { "minutes": 5, "futureOption": 42 }, "uninstalled-thing": { "a": 1 } }
            }
            """);

        var store = new ConfigStore(path);
        store.Load();
        store.SetSection(AppSettings.SectionName, store.GetSection<AppSettings>(AppSettings.SectionName));
        store.SetModuleSettings("keep-awake", new SampleSettings { Minutes = 10 });

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("keep me", root["app"]!["addedInV2"]!.GetValue<string>());
        Assert.Equal(1, root["app"]!["nested"]!["x"]!.GetValue<int>());
        Assert.Equal(42, root["modules"]!["keep-awake"]!["futureOption"]!.GetValue<int>());
        Assert.Equal(10, root["modules"]!["keep-awake"]!["minutes"]!.GetValue<int>());
        Assert.Equal(1, root["modules"]!["uninstalled-thing"]!["a"]!.GetValue<int>());
    }

    [Fact]
    public void Newer_version_number_is_kept()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, """{ "version": 7, "app": { "theme": "light" } }""");

        var store = new ConfigStore(path);
        store.Load();
        store.SetSection(AppSettings.SectionName, new AppSettings());

        Assert.Equal(7, JsonNode.Parse(File.ReadAllText(path))!["version"]!.GetValue<int>());
    }

    [Fact]
    public void Export_then_import_restores_everything_and_raises_Imported()
    {
        var path = _dir.File("settings.json");
        var exportPath = _dir.File("export.json");
        var store = new ConfigStore(path);
        store.SetSection(AppSettings.SectionName, new AppSettings { Theme = ThemePreference.Light });
        store.SetModuleSettings("m", new SampleSettings { Minutes = 33 });
        store.Export(exportPath);

        store.SetSection(AppSettings.SectionName, new AppSettings { Theme = ThemePreference.Dark });
        store.SetModuleSettings("m", new SampleSettings { Minutes = 1 });

        bool raised = false;
        store.Imported += (_, _) => raised = true;
        store.Import(exportPath);

        Assert.True(raised);
        Assert.Equal(ThemePreference.Light, store.GetSection<AppSettings>(AppSettings.SectionName).Theme);
        Assert.Equal(33, store.GetModuleSettings<SampleSettings>("m").Minutes);
        var onDisk = new ConfigStore(path);
        onDisk.Load();
        Assert.Equal(33, onDisk.GetModuleSettings<SampleSettings>("m").Minutes);
    }

    [Theory]
    [InlineData("""{ "version": 1, "features": { "installed": null } }""")]
    [InlineData("""{ "version": 1, "app": { "theme": "banana" } }""")]
    [InlineData("""{ "version": 1, "app": "light" }""")]
    [InlineData("""{ "version": 1, "modules": [] }""")]
    public void Import_rejects_invalid_known_sections_and_changes_nothing(string content)
    {
        var path = _dir.File("settings.json");
        var bad = _dir.File("bad.json");
        File.WriteAllText(bad, content);
        var store = new ConfigStore(path);
        store.SetSection(AppSettings.SectionName, new AppSettings { Theme = ThemePreference.Light });
        var before = File.ReadAllText(path);

        Assert.Throws<InvalidDataException>(() => store.Import(bad));

        Assert.Equal(ThemePreference.Light, store.GetSection<AppSettings>(AppSettings.SectionName).Theme);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void Import_that_cannot_be_written_leaves_live_settings_unchanged()
    {
        var path = _dir.File("settings.json");
        var exportPath = _dir.File("export.json");
        var store = new ConfigStore(path);
        store.SetSection(AppSettings.SectionName, new AppSettings { Theme = ThemePreference.Light });
        store.Export(exportPath);
        store.SetSection(AppSettings.SectionName, new AppSettings { Theme = ThemePreference.Dark });

        bool raised = false;
        store.Imported += (_, _) => raised = true;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) // blocks the rename
            Assert.ThrowsAny<IOException>(() => store.Import(exportPath));

        Assert.False(raised);
        Assert.Equal(ThemePreference.Dark, store.GetSection<AppSettings>(AppSettings.SectionName).Theme);
        store.Save(); // a later save must not commit the failed import
        var reloaded = new ConfigStore(path);
        reloaded.Load();
        Assert.Equal(ThemePreference.Dark, reloaded.GetSection<AppSettings>(AppSettings.SectionName).Theme);
    }

    [Fact]
    public void Load_resets_only_an_invalid_known_section_and_keeps_a_copy()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "app": { "theme": "banana" },
              "features": { "installed": ["keep-awake"] },
              "futureFeature": { "x": 1 }
            }
            """);

        var store = new ConfigStore(path);
        store.Load();

        Assert.Equal(ThemePreference.Dark, store.GetSection<AppSettings>(AppSettings.SectionName).Theme);
        Assert.True(store.HasSection("features"));
        Assert.True(store.HasSection("futureFeature"));
        Assert.Single(Directory.GetFiles(_dir.Path, "settings.invalid-*.json"));
    }

    [Fact]
    public void A_module_section_its_type_cannot_read_gives_defaults_instead_of_throwing()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, """{ "version": 1, "modules": { "m": { "minutes": "lots" } } }""");
        var store = new ConfigStore(path);
        store.Load();

        Assert.Equal(0, store.GetModuleSettings<SampleSettings>("m").Minutes);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{ "app": { "theme": "light" } }""")]
    public void Import_rejects_files_that_are_not_settings_and_changes_nothing(string content)
    {
        var path = _dir.File("settings.json");
        var bad = _dir.File("bad.json");
        File.WriteAllText(bad, content);
        var store = new ConfigStore(path);
        store.SetModuleSettings("m", new SampleSettings { Minutes = 5 });

        Assert.Throws<InvalidDataException>(() => store.Import(bad));
        Assert.Equal(5, store.GetModuleSettings<SampleSettings>("m").Minutes);
    }

    [Fact]
    public void SectionChanged_names_the_section_after_it_is_written()
    {
        var path = _dir.File("settings.json");
        var store = new ConfigStore(path);
        var seen = new List<(string Name, string OnDisk)>();
        store.SectionChanged += (_, e) => seen.Add((e.Name, File.ReadAllText(path)));

        store.SetSection(AppSettings.SectionName, new AppSettings { Theme = ThemePreference.Light });
        store.SetModuleSettings("keep-awake", new SampleSettings { Minutes = 5 });

        Assert.Equal(["app", "modules.keep-awake"], seen.Select(s => s.Name));
        Assert.Contains("\"light\"", seen[0].OnDisk, StringComparison.Ordinal); // raised after the save
        Assert.Contains("\"minutes\": 5", seen[1].OnDisk, StringComparison.Ordinal);
    }

    [Fact]
    public void SectionChanged_on_import_covers_every_section_in_either_document()
    {
        var path = _dir.File("settings.json");
        var exportPath = _dir.File("export.json");
        File.WriteAllText(exportPath, """{ "version": 1, "app": { "theme": "light" }, "modules": { "a": { "minutes": 1 } } }""");
        var store = new ConfigStore(path);
        store.SetModuleSettings("b", new SampleSettings { Minutes = 2 });
        var seen = new List<string>();
        store.SectionChanged += (_, e) => seen.Add(e.Name);

        store.Import(exportPath);

        Assert.Equal(["app", "modules.a", "modules.b"], seen.Order());
    }

    [Fact]
    public void A_failing_SectionChanged_listener_does_not_stop_others_or_the_save()
    {
        var path = _dir.File("settings.json");
        var store = new ConfigStore(path);
        bool second = false;
        store.SectionChanged += (_, _) => throw new InvalidOperationException("listener bug");
        store.SectionChanged += (_, _) => second = true;

        store.SetSection(AppSettings.SectionName, new AppSettings { Theme = ThemePreference.Light });

        Assert.True(second);
        var reloaded = new ConfigStore(path);
        reloaded.Load();
        Assert.Equal(ThemePreference.Light, reloaded.GetSection<AppSettings>(AppSettings.SectionName).Theme);
    }

    [Fact]
    public void Nothing_is_raised_when_an_import_is_rejected()
    {
        var store = new ConfigStore(_dir.File("settings.json"));
        var bad = _dir.File("bad.json");
        File.WriteAllText(bad, """{ "version": 1, "app": { "theme": "banana" } }""");
        int raised = 0;
        store.SectionChanged += (_, _) => raised++;

        Assert.Throws<InvalidDataException>(() => store.Import(bad));

        Assert.Equal(0, raised);
    }
}
