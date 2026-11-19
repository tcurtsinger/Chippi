// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.Recording;

/// <summary>
/// Settings › Screen recording: Recording (Resolution, Frame rate, Include cursor, Save folder), then Shortcuts, read-only with
/// Edit, since they're set in Settings › Shortcuts. The Settings window shows the page's title.
/// </summary>
public sealed class RecordingSettingsView : UserControl
{
    internal static readonly string[] Resolutions = ["Up to 1080p", "Original size"];
    internal static readonly string[] FrameRates = ["30 fps", "60 fps"];

    private readonly RecordingModel _model;
    private readonly ModuleContext _context;
    private readonly ComboBox _size = SettingsLayout.Combo(Resolutions);
    private readonly ComboBox _fps = SettingsLayout.Combo(FrameRates);
    private readonly ToggleButton _cursor = new();
    private readonly TextBlock _folder = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 320 };
    private readonly Button _change = new() { Content = "Change", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly Dictionary<string, ShortcutSetting> _shortcuts = [];
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private RecordingSettings? _shown;
    private bool _updating, _listening;

    public RecordingSettingsView(RecordingModel model, ModuleContext context)
    {
        _model = model;
        _context = context;
        AutomationProperties.SetName(_size, "Resolution");
        AutomationProperties.SetName(_fps, "Frame rate");
        _size.SelectionChanged += (_, _) => { if (!_updating && _size.SelectedIndex >= 0) Save(_model.Settings with { OriginalSize = _size.SelectedIndex == 1 }); };
        _fps.SelectionChanged += (_, _) => { if (!_updating && _fps.SelectedIndex >= 0) Save(_model.Settings with { FrameRate = _fps.SelectedIndex == 1 ? 60 : 30 }); };
        _cursor.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(_cursor, "Include cursor");
        // Checked and Unchecked, not Click: UI Automation's Toggle raises no Click.
        _cursor.Checked += (_, _) => { if (!_updating) Save(_model.Settings with { IncludeCursor = true }); };
        _cursor.Unchecked += (_, _) => { if (!_updating) Save(_model.Settings with { IncludeCursor = false }); };
        _folder.SetResourceReference(StyleProperty, "BodyTextStyle");
        _folder.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _change.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(_change, "Change save folder");
        _change.Click += (_, _) =>
        {
            if (PickFolder(_model.SaveFolder) is { } chosen) Save(_model.Settings with { SaveFolder = chosen });
        };
        _error.SetResourceReference(StyleProperty, "BodyTextStyle");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);

        var shortcuts = SettingsLayout.Card();
        foreach (string name in RecordingModule.ShortcutNames)
        {
            var setting = new ShortcutSetting(name, () => _context.EditShortcut(name));
            _shortcuts[name] = setting;
            shortcuts.Children.Add(SettingsLayout.Row(name, setting));
        }
        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Section("Recording", first: true),
                SettingsLayout.Card(
                    SettingsLayout.Row("Resolution", _size),
                    SettingsLayout.Row("Frame rate", _fps),
                    SettingsLayout.Row("Include cursor", _cursor),
                    SettingsLayout.Row("Save folder", new StackPanel { Orientation = Orientation.Horizontal, Children = { _folder, _change } })),
                _error,
                SettingsLayout.Section("Shortcuts"), shortcuts,
            },
        };
        Loaded += (_, _) => { Listen(true); Refresh(); };
        Unloaded += (_, _) => Listen(false);
        Refresh();
    }

    /// <summary>Opens the folder picker at <paramref name="current"/>; null when cancelled. Tests stand in for it.</summary>
    internal Func<string, string?> PickFolder { get; set; } = current =>
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Save folder", Multiselect = false };
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    };

    internal ComboBox Resolution => _size;

    internal ComboBox FrameRate => _fps;

    internal ToggleButton IncludeCursor => _cursor;

    internal TextBlock Folder => _folder;

    internal Button ChangeFolder => _change;

    internal TextBlock Error => _error;

    internal ShortcutSetting ShortcutOf(string name) => _shortcuts[name];

    /// <summary>"Videos\Chippi" for a folder in Videos, the rest of the path under the profile, or the whole path.</summary>
    internal static string Display(string folder)
    {
        string full = folder.TrimEnd('\\');
        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos).TrimEnd('\\');
        if (videos.Length > 0 && (full.Equals(videos, StringComparison.OrdinalIgnoreCase) || full.StartsWith(videos + "\\", StringComparison.OrdinalIgnoreCase)))
            return "Videos" + full[videos.Length..];
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        if (profile.Length > 0 && full.StartsWith(profile + "\\", StringComparison.OrdinalIgnoreCase))
            return full[(profile.Length + 1)..];
        return full;
    }

    private void Listen(bool on)
    {
        if (on == _listening) return;
        _listening = on;
        if (on) { _model.Changed += OnModelChanged; _context.Shortcuts.Changed += OnShortcutsChanged; }
        else { _model.Changed -= OnModelChanged; _context.Shortcuts.Changed -= OnShortcutsChanged; }
    }

    // The model changes every second while recording; redraw only when its settings did (the island's quality pickers).
    private void OnModelChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => { if (_listening) OnModelChanged(); }); return; }
        if (!ReferenceEquals(_shown, _model.Settings)) Refresh();
    }

    private void OnShortcutsChanged()
    {
        if (Dispatcher.CheckAccess()) RefreshShortcuts();
        else Dispatcher.BeginInvoke(() => { if (_listening) RefreshShortcuts(); });
    }

    private void Refresh()
    {
        var settings = _shown = _model.Settings;
        _updating = true;
        try
        {
            _size.SelectedIndex = settings.OriginalSize ? 1 : 0;
            _fps.SelectedIndex = settings.FrameRate == 60 ? 1 : 0;
            _cursor.IsChecked = settings.IncludeCursor;
            _cursor.Content = settings.IncludeCursor ? "On" : "Off";
        }
        finally { _updating = false; }
        string folder = _model.SaveFolder;
        _folder.Text = Display(folder);
        _folder.ToolTip = folder;
        RefreshShortcuts();
    }

    private void RefreshShortcuts()
    {
        foreach (var (name, view) in _shortcuts)
        {
            var slot = _context.Shortcuts.List.FirstOrDefault(s => s.Owner == RecordingModule.Id && s.Name == name);
            view.Show(slot?.Shortcut, slot?.Error);
        }
    }

    private void Save(RecordingSettings settings)
    {
        bool saved = _model.SaveSettings(settings);
        Refresh();
        _error.Text = saved ? "" : "Save failed";
        _error.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        if (!saved) UIElementAutomationPeer.FromElement(_error)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
