// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.Core.Modules;
using Chippi.Features.Clipboard.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Clipboard.Tests;

public sealed partial class ModuleTests
{
    [Fact]
    public async Task A_rejected_recording_change_preserves_the_listener_and_reports_the_error_until_retry()
    {
        await StartHistoryAsync();
        WpfTestThread.Run(() =>
        {
            var view = (ClipboardView)_historyUi.Spec!.CreateSection();
            Load(view);
            try
            {
                var turnOn = Assert.IsType<Button>(view.InitialFocus);
                _settings.RejectWrites = true;
                ClickWidgetButton(turnOn);
                Flush(view);
                Assert.True(Shows(view, "Save failed"));
                Assert.True(Shows(view, "Recording off"));
                Assert.False(_history.Settings.Recording);
                Assert.Equal(0, _hub.Leases);

                _settings.RejectWrites = false;
                ClickWidgetButton(turnOn);
                Flush(view);
                Assert.False(Shows(view, "Save failed"));
                Assert.True(_history.Settings.Recording);
                Assert.Equal(1, _hub.Leases);

                _settings.RejectWrites = true;
                _history.SetRecording(false);
                Flush(view);
                Assert.True(Shows(view, "Save failed"));
                Assert.True(Shows(view, "No items"));
                Assert.True(_settings.Get<ClipboardHistorySettings>().Recording);
                Assert.True(_history.Settings.Recording);
                Assert.Equal(1, _hub.Leases);
            }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });
    }

    [Fact]
    public async Task Recording_off_keeps_saved_clips_and_filtered_clear_removes_only_the_confirmed_matches()
    {
        await StartHistoryAsync();
        _history.SetRecording(true);
        var history = _history.History!;
        foreach (var text in new[] { "invoice pinned", "invoice draft", "meeting note" })
        {
            _hub.CopyText(text);
            await history.IdleAsync();
        }
        await history.PinAsync(history.Entries.Single(e => e.Text == "invoice pinned").Id, true);
        _history.SetRecording(false);
        WpfTestThread.Run(() =>
        {
            var view = (ClipboardView)_historyUi.Spec!.CreateSection();
            Load(view);
            try
            {
                Assert.True(Shows(view.Header, "Recording off"));
                Assert.False(Shows(view, "Recording off"));
                Assert.True(Shows(view, "meeting note"));
                var search = Assert.IsType<SearchBox>(view.InitialFocus);
                var turnOn = Find<Button>(view.Header).Single(b => AutomationProperties.GetName(b) == "Turn on");
                Assert.True(IsShown(turnOn));
                var more = Find<Button>(view.Header).Single(b => AutomationProperties.GetName(b) == "More");
                Assert.True(IsShown(more));
                Popup? current = null;
                view.ShowPopup = popup => current = popup; // Exercise the actual commands without opening a desktop popup.
                Button Action(string label) => Find<Button>(current!.Child).Single(b => Equals(b.Content, label));
                search.Text = "pinned";
                ClickWidgetButton(more);
                Assert.True(Action("All history").IsEnabled);
                Assert.False(Action("Clear matches").IsEnabled);
                Assert.True(Action("Erase all").IsEnabled);
                search.Text = "invoice";
                ClickWidgetButton(more);
                ClickWidgetButton(Action("Clear matches"));
                Assert.Contains(Find<TextBlock>(current!.Child), text => text.Text == "Clear 1 matching unpinned clip?");
                ClickWidgetButton(Action("Clear"));
                history.IdleAsync().GetAwaiter().GetResult();
                Assert.Equal(["invoice pinned", "meeting note"], history.Entries.Select(e => e.Text!).Order(StringComparer.Ordinal).ToArray());
                Assert.False(_history.Settings.Recording);
                ClickWidgetButton(turnOn);
                Assert.True(_history.Settings.Recording);
            }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });
    }

    [Fact]
    public async Task All_history_opens_matching_results_and_back_retraces_clean_URL()
    {
        string? route = null;
        var context = new ModuleContext(ClipboardHistoryModule.Id, _settings, NullLogger.Instance, new Island(), _historyUi)
        {
            Clipboard = _hub, DataDirectory = _folder.Path, OpenSettings = section => route = section,
        };
        await _history.StartAsync(context, default);
        await StartCleanAsync();
        _history.SetRecording(true);
        for (int i = 0; i < 22; i++)
        {
            _hub.CopyText($"Invoice {i:D2}\nSecond line");
            await _history.History!.IdleAsync();
        }

        WpfTestThread.Run(() =>
        {
            var spec = _historyUi.Spec!;
            var view = (ClipboardView)spec.CreateSection();
            Load(view);
            void Fit() { view.Measure(new Size(496, 304)); view.Arrange(new Rect(0, 0, 496, 304)); view.UpdateLayout(); }
            try
            {
                var search = Find<SearchBox>(view).Single();
                var all = Find<Button>(view).Single(b => Equals(b.Content, "All history"));
                search.Text = "Invoice 03";
                Fit();
                Assert.False(IsShown(all));
                ClickWidgetButton(Find<Button>(view).Single(b => AutomationProperties.GetName(b).StartsWith("Invoice 03, ", StringComparison.Ordinal)));
                Assert.True(Shows(view, "Text · 2 lines"));
                Assert.True(Shows(view, "Invoice 03\nSecond line"));
                search.Text = "invoice";
                Fit();
                Assert.True(IsShown(all));
                ClickWidgetButton(all);
                Assert.Equal("history", route);
                var settings = (ClipboardHistorySettingsPage)_historyUi.SettingsPage!(route);
                Assert.Equal("invoice", Find<SearchBox>(settings).Single().Text);
                Assert.Empty(Find<SearchBox>((ClipboardHistorySettingsPage)_historyUi.SettingsPage!(null)).Single().Text);

                ClickWidgetButton(Find<Button>(view.Header).Single(b => AutomationProperties.GetName(b) == "Clean URL"));
                Assert.True(spec.HandleBack!());
                Assert.Same(search, spec.InitialFocus!());
                Assert.Equal("invoice", search.Text);
                Assert.False(spec.HandleBack!());
            }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });
    }

    private static void ClickWidgetButton(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
