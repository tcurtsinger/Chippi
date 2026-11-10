// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.Core.Modules;
using Microsoft.Win32;

namespace Chippi.Features.Recording;

public sealed class RecordingSettingsView : UserControl
{
    public RecordingSettingsView(RecordingModel model, ModuleContext context)
    {
        var body = new StackPanel { Margin = new(12) };
        body.Children.Add(new TextBlock { Text = "Screen recording", FontSize = 22, Margin = new(0, 0, 0, 16) });
        var size = new ComboBox { ItemsSource = new[] { "Up to 1080p", "Original size" }, SelectedIndex = model.Settings.OriginalSize ? 1 : 0, Margin = new(0, 4, 0, 12) };
        size.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
        body.Children.Add(RecordingView.Label("Resolution")); body.Children.Add(size);
        size.SelectionChanged += (_, _) => model.SaveSettings(model.Settings with { OriginalSize = size.SelectedIndex == 1 });
        var fps = new ComboBox { ItemsSource = new[] { "30 fps (lower impact)", "60 fps" }, SelectedIndex = model.Settings.FrameRate == 60 ? 1 : 0, Margin = new(0, 4, 0, 12) };
        fps.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
        body.Children.Add(RecordingView.Label("Frame rate")); body.Children.Add(fps);
        fps.SelectionChanged += (_, _) => model.SaveSettings(model.Settings with { FrameRate = fps.SelectedIndex == 1 ? 60 : 30 });
        var cursor = new CheckBox { Content = "Include cursor", IsChecked = model.Settings.IncludeCursor, Margin = new(0, 6, 0, 12) };
        cursor.Click += (_, _) => model.SaveSettings(model.Settings with { IncludeCursor = cursor.IsChecked == true }); body.Children.Add(cursor);
        var folder = RecordingView.Label(model.SaveFolder); folder.TextWrapping = TextWrapping.Wrap; body.Children.Add(folder);
        Button SettingsButton(string name, Action action) { var button = RecordingView.Button(name, action); button.SetResourceReference(StyleProperty, "SecondaryButtonStyle"); return button; }
        body.Children.Add(SettingsButton("Change recording folder", () =>
        {
            var dialog = new OpenFolderDialog { Title = "Save recordings to", InitialDirectory = model.SaveFolder };
            if (dialog.ShowDialog() == true && model.SaveSettings(model.Settings with { SaveFolder = dialog.FolderName })) folder.Text = model.SaveFolder;
        }));
        body.Children.Add(RecordingView.Label("Shortcuts also work in the Radial menu."));
        foreach (string name in RecordingModule.ShortcutNames) body.Children.Add(SettingsButton(name + " · Edit shortcut", () => context.EditShortcut(name)));
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
