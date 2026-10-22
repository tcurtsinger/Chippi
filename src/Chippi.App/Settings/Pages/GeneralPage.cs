// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Diagnostics;
using Chippi.UI.Controls;
using Chippi.UI.Themes;
using Microsoft.Win32;

namespace Chippi.App.Settings.Pages;

/// <summary>General: theme, run at startup, and settings export and import (brief §4.5).</summary>
internal sealed class GeneralPage : UserControl
{
    private readonly ShellContext _shell;
    private TextBlock _transferMessage = null!;

    public GeneralPage(ShellContext shell)
    {
        _shell = shell;
        Build();
    }

    private void Build()
    {
        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header("General", "How Chippi looks and starts, and where its settings live."));

        // Theme, applied live. Appearance points here rather than holding a second copy.
        var theme = SettingsUi.Segments("Theme",
            [("Dark", ThemePreference.Dark), ("Light", ThemePreference.Light), ("Follow Windows", ThemePreference.System)],
            SettingsUi.ReadApp(_shell.Config).Theme);
        theme.Width = 440;
        theme.SelectionChanged += (_, _) =>
        {
            if (theme.SelectedItem is SegmentItem { Value: ThemePreference choice })
            {
                ThemeManager.Apply(choice);
                SettingsUi.SaveApp(_shell.Config, s => s.Theme = choice);
            }
        };
        page.Children.Add(SettingsUi.Row("Theme", "Dark is the default. The island stays dark in every theme.", theme));

        page.Children.Add(StartupRow());
        page.Children.Add(TransferRow());
        Content = page;
    }

    private Border StartupRow()
    {
        var message = SettingsUi.Message();
        bool enabled;
        try
        {
            enabled = _shell.Startup.IsEnabled();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            enabled = false;
            SettingsUi.Show(message, "Windows would not let Chippi read its startup entry.", problem: true);
        }

        var toggle = SettingsUi.Switch(enabled);
        toggle.Click += (_, _) =>
        {
            bool want = toggle.IsChecked == true;
            try
            {
                _shell.Startup.SetEnabled(want);
                message.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                Log.Warn("Run at startup could not be changed", ex);
                toggle.IsChecked = !want;
                SettingsUi.Show(message, "Windows would not let Chippi change its startup entry.", problem: true);
            }
        };
        return SettingsUi.Row("Run at startup", "Starts quietly in the tray when you sign in.", toggle, message);
    }

    private Border TransferRow()
    {
        var message = _transferMessage = SettingsUi.Message();
        var export = SettingsUi.Button("Export…");
        var import = SettingsUi.Button("Import…");
        export.Click += (_, _) => Export(message);
        import.Click += async (_, _) => await ImportAsync(message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(export);
        buttons.Children.Add(import);
        return SettingsUi.Row("Settings file", _shell.Config.FilePath, buttons, message);
    }

    private void Export(TextBlock message)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Chippi settings",
            FileName = $"chippi-settings-{DateTime.Now:yyyy-MM-dd}.json",
            Filter = "Chippi settings (*.json)|*.json",
            AddExtension = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        try
        {
            _shell.Config.Export(dialog.FileName);
            SettingsUi.Show(message, $"Exported to {dialog.FileName}", problem: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Settings export failed", ex);
            SettingsUi.Show(message, $"Couldn't export: {ex.Message}", problem: true);
        }
    }

    private async Task ImportAsync(TextBlock message)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Chippi settings",
            Filter = "Chippi settings (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        try
        {
            _shell.Config.Import(dialog.FileName);
        }
        catch (InvalidDataException ex)
        {
            SettingsUi.Show(message, $"Nothing changed. {ex.Message}", problem: true);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Settings import failed", ex);
            SettingsUi.Show(message, $"Nothing changed. {ex.Message}", problem: true);
            return;
        }

        // The file is in; bring the running app in line with it.
        var app = SettingsUi.ReadApp(_shell.Config);
        ThemeManager.Apply(app.Theme);
        Motion.Override = app.ReduceMotion;
        await _shell.Registry.ReloadAsync();
        Build(); // the rows show the imported values
        SettingsUi.Show(_transferMessage, $"Imported from {dialog.FileName}", problem: false);
    }
}
