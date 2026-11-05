// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;
using System.Diagnostics;
using System.IO;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.CommandBar.Views;
using Chippi.Features.CommandBar.Windows;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.CommandBar.Tests;

/// <summary>
/// Times the Command bar on this PC's real Start menu, windows and Windows Search: only reads, and the report holds counts
/// and milliseconds, never a name. Runs only when CHIPPI_COMMAND_BAR_TIMINGS names the report.
/// </summary>
public sealed class CommandBarTimings
{
    [Fact]
    public async Task Report()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_COMMAND_BAR_TIMINGS") is not { Length: > 0 } report)
            return;
        var lines = new List<string>();
        using var pc = new WindowsPc();
        var clock = Stopwatch.StartNew();
        var read = new TaskCompletionSource();
        pc.AppsChanged += () => read.TrySetResult();
        pc.RefreshApps();
        await read.Task.WaitAsync(TimeSpan.FromSeconds(30));
        lines.Add($"Start menu read: {pc.Apps.Count} apps in {clock.ElapsedMilliseconds} ms (off the UI thread, at start and on each open)");
        clock.Restart();
        var windows = pc.Windows();
        lines.Add($"Windows listed: {windows.Count} in {clock.ElapsedMilliseconds} ms (on each open)");

        var search = new CommandSearch(pc, () => [], _ => [], () => new CommandBarSettings(), new Uses(), _ => Task.CompletedTask);
        search.Find("x", windows, []); // the fixed lists are read once
        var typing = new List<double>();
        foreach (var word in new[] { "visual studio", "chrome", "bluetooth", "party", "12*7", "5 km in mi" })
            for (int length = 1; length <= word.Length; length++)
            {
                clock.Restart();
                search.Find(word[..length], windows, []);
                typing.Add(clock.Elapsed.TotalMilliseconds);
            }
        typing.Sort();
        lines.Add($"Finding as you type: {typing.Count} keystrokes, median {typing[typing.Count / 2]:0.00} ms, max {typing[^1]:0.00} ms");

        foreach (var text in new[] { "chr", "invoice", "screen", "notes" })
        {
            clock.Restart();
            var files = await pc.FindFilesAsync(text, 8, CancellationToken.None);
            lines.Add($"Windows Search \"{text.Length} letters\": {files.Count} files in {clock.ElapsedMilliseconds} ms (after a 120 ms pause in typing)");
        }

        clock.Restart();
        int icons = 0;
        foreach (var app in pc.Apps.Take(8))
            icons += await pc.AppIconAsync(app, 30) is null ? 0 : 1;
        lines.Add($"App icons: {icons} of 8 read in {clock.ElapsedMilliseconds} ms (first time; kept after)");

        WpfTestThread.Run(() =>
        {
            var ui = new Ui();
            var module = new CommandBarModule(() => pc, () => new Hotkey());
            module.StartAsync(new ModuleContext(CommandBarModule.Id, new Settings(), NullLogger.Instance, new IslandActivityHub().CreatePublisher(CommandBarModule.Id), ui)
            {
                DataDirectory = Path.Combine(Path.GetTempPath(), "chippi-tests", "command-bar-timings"),
            }, default).GetAwaiter().GetResult();
            var page = (CommandBarPage)ui.Tile!.CreateSection();
            var renders = new List<double>();
            clock.Restart();
            page.Refresh();
            page.Measure(new System.Windows.Size(496, 296));
            page.Arrange(new System.Windows.Rect(0, 0, 496, 296));
            page.UpdateLayout();
            lines.Add($"Page filled and laid out: {clock.ElapsedMilliseconds} ms");
            foreach (var word in new[] { "visual studio", "settings" })
                for (int length = 1; length <= word.Length; length++)
                {
                    clock.Restart();
                    page.Field.Text = word[..length];
                    page.UpdateLayout();
                    renders.Add(clock.Elapsed.TotalMilliseconds);
                }
            renders.Sort();
            lines.Add($"Rows redrawn as you type: {renders.Count} keystrokes, median {renders[renders.Count / 2]:0.00} ms, max {renders[^1]:0.00} ms");
            module.StopAsync(default).GetAwaiter().GetResult();
        });
        File.WriteAllLines(report, lines);
    }

    private sealed class Settings : IModuleSettings
    {
        private CommandBarSettings _value = new();
        public T Get<T>() where T : class, new() => (T)(object)_value;
        public void Save<T>(T value) where T : class => _value = (CommandBarSettings)(object)value;
    }

    private sealed class Ui : IModuleUi
    {
        public IslandTileSpec? Tile;
        public IIslandTile AddIslandTile(IslandTileSpec spec)
        {
            Tile = spec;
            return new IslandTileContribution(CommandBarModule.Id, spec);
        }

        public void SetSettingsPage(Func<string?, object> page) { }
    }

    private sealed class Hotkey : IHotkey
    {
        public bool Register(KeyShortcut shortcut, Action pressed) => true;
        public void Dispose() { }
    }
}
