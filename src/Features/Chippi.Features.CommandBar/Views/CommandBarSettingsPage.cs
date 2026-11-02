// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;

namespace Chippi.Features.CommandBar.Views;

/// <summary>
/// Settings › Command bar (docs/phase-5.5/command-bar.md): Web search (Google, Bing or DuckDuckGo); Results, a switch per
/// kind, all on; Hidden, each hidden result with what it is under it and Show, or "None".
/// </summary>
public sealed class CommandBarSettingsPage : UserControl
{
    /// <summary>Results' rows, in the order of <see cref="ResultSwitch"/>.</summary>
    internal static readonly IReadOnlyList<(ResultSwitch Kind, string Name)> Kinds =
    [
        (ResultSwitch.Apps, "Apps"),
        (ResultSwitch.Windows, "Open windows"),
        (ResultSwitch.Files, "Files and folders"),
        (ResultSwitch.Settings, "Windows Settings"),
        (ResultSwitch.Chippi, "Chippi"),
        (ResultSwitch.Clipboard, "Clipboard history"),
        (ResultSwitch.Math, "Math and conversions"),
        (ResultSwitch.Emoji, "Emoji"),
        (ResultSwitch.Web, "Web search"),
    ];

    private static readonly string[] Engines = ["Google", "Bing", "DuckDuckGo"];

    private readonly CommandBarModule _module;
    private readonly ComboBox _web = SettingsLayout.Combo(Engines);
    private readonly Dictionary<ResultSwitch, ToggleButton> _switches = [];
    private readonly Card _hidden = SettingsLayout.Card();
    private string _hiddenShown = "\0";
    private bool _updating;

    public CommandBarSettingsPage(CommandBarModule module)
    {
        _module = module;
        AutomationProperties.SetName(_web, "Web search");
        _web.SelectionChanged += (_, _) =>
        {
            if (!_updating && _web.SelectedIndex >= 0)
                _module.SetWebSearch((WebEngine)_web.SelectedIndex);
        };
        var results = SettingsLayout.Card();
        foreach (var (kind, name) in Kinds)
        {
            var toggle = new ToggleButton();
            toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
            AutomationProperties.SetName(toggle, name);
            // Checked and Unchecked, not Click: UI Automation's Toggle raises no Click (AGENTS.md).
            toggle.Checked += (_, _) => { if (!_updating) _module.SetOn(kind, true); };
            toggle.Unchecked += (_, _) => { if (!_updating) _module.SetOn(kind, false); };
            _switches[kind] = toggle;
            results.Children.Add(SettingsLayout.Row(name, toggle));
        }
        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Card(SettingsLayout.Row("Web search", _web)),
                SettingsLayout.Section("Results"), results,
                SettingsLayout.Section("Hidden"), _hidden,
            },
        };
        Loaded += (_, _) =>
        {
            _module.Changed += Refresh;
            Refresh();
        };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Refresh);
            return;
        }
        var settings = _module.Settings;
        _updating = true;
        try
        {
            _web.SelectedIndex = (int)settings.WebSearch;
            foreach (var (kind, toggle) in _switches)
                toggle.IsChecked = !settings.Off.Contains(kind);
        }
        finally { _updating = false; }
        string hidden = string.Join("\n", settings.Hidden.Select(h => h.Key));
        if (hidden == _hiddenShown)
            return;
        _hiddenShown = hidden;
        _hidden.Children.Clear();
        foreach (var result in settings.Hidden)
        {
            var show = new Button { Content = "Show" };
            show.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            AutomationProperties.SetName(show, $"Show {result.Name}");
            string key = result.Key;
            show.Click += (_, _) => _module.Unhide(key);
            _hidden.Children.Add(SettingsLayout.Row(result.Name, show, result.Kind));
        }
        if (settings.Hidden.Count == 0)
            _hidden.Children.Add(SettingsLayout.Empty("None"));
    }
}

/// <summary>The shortcut's row on Settings › Shortcuts: Ctrl+Alt+Space by default, with Change and Remove, or Set once removed.</summary>
public sealed class ShortcutRow : UserControl
{
    private readonly CommandBarModule _module;
    private readonly string _title;
    private readonly StackPanel _body = new();
    private bool _capturing;

    public ShortcutRow(CommandBarModule module, string title)
    {
        _module = module;
        _title = title;
        Content = _body;
        Focusable = true;
        PreviewKeyDown += Capture;
        Loaded += (_, _) =>
        {
            _module.Changed += Refresh;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _module.Changed -= Refresh;
            Cancel();
        };
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Refresh);
            return;
        }
        if (_capturing)
            return;
        var shortcut = _module.Settings.Shortcut;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_module.ShortcutError is { } error
            ? Text(error, "WarningTextBrush")
            : Text(shortcut?.ToString() ?? "Not set", shortcut is null ? "TextSecondaryBrush" : "TextPrimaryBrush"));
        row.Children.Add(Button(shortcut is null ? "Set" : "Change", Begin));
        if (shortcut is not null)
            row.Children.Add(Button("Remove", _module.RemoveShortcut));
        Show(row);
    }

    private void Show(UIElement controls)
    {
        _body.Children.Clear();
        var card = SettingsLayout.Card(SettingsLayout.Row(_title, controls));
        card.SetResourceReference(StyleProperty, "CardRowsStyle");
        _body.Children.Add(card);
    }

    private void Begin()
    {
        _capturing = true;
        _module.CaptureShortcut(true);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { Text("New shortcut", "TextSecondaryBrush"), Button("Cancel", Cancel) } };
        Show(row);
        Focus();
    }

    private void Cancel()
    {
        if (!_capturing)
            return;
        _capturing = false;
        _module.CaptureShortcut(false);
        Refresh();
    }

    private void Capture(object sender, KeyEventArgs e)
    {
        if (!_capturing)
            return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            Cancel();
            return;
        }
        if (MicShortcut.From(e) is not { } shortcut)
            return;
        _capturing = false;
        _module.CaptureShortcut(false);
        _module.ChangeShortcut(shortcut);
        Refresh();
    }

    private static TextBlock Text(string text, string brush)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(StyleProperty, "BodyTextStyle");
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0) };
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(button, $"{label} {_title} shortcut");
        button.Click += (_, _) => action();
        return button;
    }
}
