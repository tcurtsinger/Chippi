// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Chippi.Core.Modules;
using Chippi.Media;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.Features.MediaTools;

internal sealed class MediaToolsSettingsView : ContentControl
{
    public MediaToolsSettingsView(MediaToolsModule module, ModuleContext context)
    {
        var body = new StackPanel { MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Stretch };
        var card = new Card(); body.Children.Add(card);
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap }; message.SetResourceReference(StyleProperty, "CaptionTextStyle");
        SettingRow Row(string title, FrameworkElement control)
        {
            AutomationProperties.SetName(control, title);
            var row = new SettingRow { Header = title, Content = control }; card.Children.Add(row); return row;
        }
        SettingRow? folderRow = null;
        bool refreshing = false;
        void ShowFolder() { if (folderRow != null) folderRow.Description = string.IsNullOrWhiteSpace(module.Settings.Options.OutputFolder) ? "Beside originals" : module.Settings.Options.OutputFolder; }
        var folderButtons = new StackPanel { Orientation = Orientation.Horizontal };
        Button Button(string name, Action action) { var button = MediaToolsView.Button(name, action); button.SetResourceReference(StyleProperty, "SecondaryButtonStyle"); return button; }
        folderButtons.Children.Add(Button("Change", () =>
        {
            using var hold = context.HoldIslandOpen();
            var dialog = new OpenFolderDialog { Title = "Media output folder" };
            if (dialog.ShowDialog() == true) { if (!module.SaveOptions(module.Settings.Options with { OutputFolder = dialog.FolderName })) message.Text = "Save failed"; ShowFolder(); }
        }));
        folderButtons.Children.Add(Button("Reset", () => { if (!module.SaveOptions(module.Settings.Options with { OutputFolder = "" })) message.Text = "Save failed"; ShowFolder(); }));
        folderRow = Row("Output folder", folderButtons); ShowFolder();
        var metadata = new CheckBox { IsChecked = module.Settings.Options.KeepMetadata, VerticalAlignment = VerticalAlignment.Center };
        Row("Keep metadata", metadata).Description = "Retain supported author, camera and location tags";
        var quality = new ComboBox { ItemsSource = Enum.GetNames<MediaQuality>(), SelectedIndex = (int)module.Settings.Options.Quality, MinWidth = 150 };
        quality.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
        Row("Default level", quality);
        var background = new TextBox { Text = $"#{module.Settings.Options.Background:X6}", Width = 150, Height = 30 };
        background.SetResourceReference(StyleProperty, "TextFieldStyle");
        background.LostKeyboardFocus += (_, _) =>
        {
            if (MediaToolsView.TryColor(background.Text, out uint color))
            { if (!module.SaveOptions(module.Settings.Options with { Background = color })) message.Text = "Save failed"; }
            else message.Text = "Enter a six-digit color";
        };
        Row("JPEG background", background);
        var shortcuts = new List<(string Name, ShortcutSetting View)>();
        foreach (string name in MediaToolsModule.ShortcutNames)
        {
            var shortcut = new ShortcutSetting(name, () => context.EditShortcut(name));
            var slot = context.Shortcuts.List.FirstOrDefault(s => s.Owner == MediaToolsModule.Id && s.Name == name);
            shortcut.Show(slot?.Shortcut, slot?.Error); Row(name, shortcut);
            shortcuts.Add((name, shortcut));
        }
        metadata.Checked += (_, _) => { if (!refreshing && !module.SaveOptions(module.Settings.Options with { KeepMetadata = true })) { message.Text = "Save failed"; Refresh(); } };
        metadata.Unchecked += (_, _) => { if (!refreshing && !module.SaveOptions(module.Settings.Options with { KeepMetadata = false })) { message.Text = "Save failed"; Refresh(); } };
        quality.SelectionChanged += (_, _) => { if (!refreshing && quality.SelectedIndex >= 0 && !module.SaveOptions(module.Settings.Options with { Quality = (MediaQuality)quality.SelectedIndex })) { message.Text = "Save failed"; Refresh(); } };
        body.Children.Add(message);
        void Refresh()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
            refreshing = true;
            ShowFolder();
            metadata.IsChecked = module.Settings.Options.KeepMetadata;
            quality.SelectedIndex = (int)module.Settings.Options.Quality;
            background.Text = $"#{module.Settings.Options.Background:X6}";
            card.IsEnabled = module.Batch is { IsRunning: false, IsInspecting: false };
            foreach (var (name, view) in shortcuts)
            {
                var slot = context.Shortcuts.List.FirstOrDefault(s => s.Owner == MediaToolsModule.Id && s.Name == name);
                view.Show(slot?.Shortcut, slot?.Error);
            }
            refreshing = false;
        }
        Loaded += (_, _) => { module.Changed -= Refresh; module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => module.Changed -= Refresh;
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
