// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.App.Island;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.UI.Controls;

namespace Chippi.App.Settings.Pages;

/// <summary>Peek's choices on Island &amp; pet. Shortcut recording stays in the shared editor.</summary>
internal sealed class PeekSettingsSection : StackPanel
{
    private readonly ConfigStore _config;
    private readonly IShortcutService _shortcuts;
    private readonly Func<IReadOnlyList<IslandTileGroup>> _panels;
    private readonly ShortcutSetting _shortcut;
    private readonly ComboBox _panel = SettingsUi.Styled(new ComboBox { MinWidth = 180, MaxWidth = 300 }, "HopperComboBoxStyle");
    private readonly SegmentedControl _mode = SettingsUi.Segments("Peek mode", new[] { ("Hold", PeekMode.Hold), ("Toggle", PeekMode.Toggle) }, PeekMode.Hold);
    private readonly TextBlock _unavailable = SettingsUi.Message(), _hint = SettingsUi.Message(), _error = SettingsUi.Message();
    private bool _refreshing;
    private string? _choices;

    internal PeekSettingsSection(ConfigStore config, IShortcutService shortcuts, Func<IReadOnlyList<IslandTileGroup>> panels, Action edit)
    {
        _config = config; _shortcuts = shortcuts; _panels = panels;
        _shortcut = new ShortcutSetting("Peek", edit);
        AutomationProperties.SetName(_panel, "Default Peek panel");
        Children.Add(SettingsUi.Section("Peek"));
        Children.Add(SettingsUi.Group(
            SettingsUi.Row("Shortcut", "", _shortcut),
            SettingsUi.Row("Default panel", "", _panel, _unavailable),
            SettingsUi.Row("Mode", "", _mode, _hint)));
        Children.Add(_error);
        _panel.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _panel.SelectedItem is ComboBoxItem item)
                Save(s => s.PeekPage = item.Tag as string);
        };
        _mode.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _mode.SelectedItem is SegmentItem { Value: PeekMode mode })
                Save(s => s.PeekMode = mode);
        };
        Loaded += (_, _) =>
        {
            _config.SectionChanged -= OnSettingsChanged; _config.SectionChanged += OnSettingsChanged;
            _config.Imported -= OnImported; _config.Imported += OnImported;
            _shortcuts.Changed -= Refresh; _shortcuts.Changed += Refresh;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _config.SectionChanged -= OnSettingsChanged;
            _config.Imported -= OnImported;
            _shortcuts.Changed -= Refresh;
        };
        Refresh();
    }

    private void OnSettingsChanged(object? sender, ConfigSectionChangedEventArgs e) { if (e.Name == AppSettings.SectionName) Refresh(); }
    private void OnImported(object? sender, EventArgs e) => Refresh();

    private void Save(Action<AppSettings> change)
    {
        try { SettingsUi.SaveApp(_config, change); _error.Visibility = Visibility.Collapsed; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { SettingsUi.Show(_error, "Peek settings save failed", true); _error.ToolTip = ex.Message; }
        Refresh();
    }

    internal void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var app = SettingsUi.ReadApp(_config);
        var choices = Choices(_panels());
        bool unavailable = app.PeekPage is not null && !choices.Any(c => c.Key == app.PeekPage);
        string identity = string.Join("|", choices.Select(c => c.Key + ":" + c.Label)) + "|" + (unavailable ? app.PeekPage : "");
        _refreshing = true;
        try
        {
            if (_choices != identity)
            {
                _choices = identity; _panel.Items.Clear();
                foreach (var choice in choices) _panel.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Key });
                if (unavailable) _panel.Items.Add(new ComboBoxItem { Content = "Unavailable panel", Tag = app.PeekPage, IsEnabled = false });
            }
            _panel.SelectedItem = _panel.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, app.PeekPage));
            var mode = Enum.IsDefined(app.PeekMode) ? app.PeekMode : PeekMode.Hold;
            _mode.SelectedItem = _mode.Items.OfType<SegmentItem>().First(i => Equals(i.Value, mode));
            SettingsUi.Show(_hint, mode == PeekMode.Hold ? "Release the shortcut to collapse Chippi" : "Press again or Esc to collapse Chippi; clicking away leaves Peek open", false);
            if (unavailable) SettingsUi.Show(_unavailable, "Panel unavailable · Peek opens Home", false); else _unavailable.Visibility = Visibility.Collapsed;
            var shortcut = _shortcuts.List.FirstOrDefault(s => s.Owner == "peek" && s.Name == "Peek");
            _shortcut.Show(shortcut?.Shortcut ?? app.PeekShortcut, shortcut?.Error
                ?? (app.IslandEnabled && app.PeekShortcut != null && shortcut?.Held != true ? "Peek shortcut failed" : null));
        }
        finally { _refreshing = false; }
    }

    internal static IReadOnlyList<(string? Key, string Label)> Choices(IReadOnlyList<IslandTileGroup> panels)
    {
        var choices = new List<(string? Key, string Label)> { (null, "Home"), (IslandPanelWindow.NowPlayingKey, "Now Playing") };
        if (panels.Any(p => IslandTool.IsAudioPage(p.Key))) choices.Add((IslandTool.Audio, "Audio"));
        foreach (var panel in panels.Where(p => p.Key != IslandPanelWindow.NowPlayingKey))
            choices.Add((panel.Key, IslandTool.IsAudioPage(panel.Key) ? "Audio · " + IslandTool.AudioPageName(panel.Key) : panel.Name));
        return choices.DistinctBy(c => c.Key).ToArray();
    }
}
