// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.Features.Clipboard.Views;

/// <summary>Settings › Clipboard history (design notes #52, #55): Recording, and apps to skip. Nothing for fixed behavior.</summary>
public sealed class ClipboardHistorySettingsPage : UserControl
{
    private readonly ClipboardHistoryModule _module;
    private readonly Card _apps = new();
    private readonly Action _syncRecording;
    private readonly Button _add = SettingsUi.Button("Add app");

    public ClipboardHistorySettingsPage(ClipboardHistoryModule module)
    {
        _module = module;
        var recording = SettingsUi.Switch("Recording", () => _module.Settings.Recording, _module.SetRecording, out _syncRecording);
        var first = new Card { Children = { new SettingRow { Header = "Recording", Content = recording } } };
        _add.Click += (_, _) => OpenAdd();
        Content = new StackPanel { Children = { first, SettingsUi.Heading("Apps to skip", _add), _apps } };
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
        _syncRecording();
        _apps.Children.Clear();
        foreach (var path in _module.Settings.AppsToSkip)
            _apps.Children.Add(SettingsUi.ListRow(Monogram(AppName(path)), AppName(path), path, () => Save(_module.Settings.AppsToSkip.Where(p => p != path))));
        if (_apps.Children.Count == 0)
        {
            var none = SettingsUi.Text("No apps");
            none.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            _apps.Children.Add(new Border { MinHeight = 48, Padding = new Thickness(16, 0, 16, 0), Child = none });
        }
    }

    private void Save(IEnumerable<string> apps) => _module.Save(_module.Settings with { AppsToSkip = [.. apps] });

    /// <summary>Add app: the running apps with a window, then Browse… for an .exe (notes #52).</summary>
    private void OpenAdd() => CreateAddMenu(RunningApps()).IsOpen = true;

    internal Popup CreateAddMenu(IEnumerable<(string Name, string Path)> apps)
    {
        var items = apps.Where(a => !_module.Settings.AppsToSkip.Contains(a.Path, StringComparer.OrdinalIgnoreCase))
            .Select(app => (app.Name, true, (Action)(() => Save([.. _module.Settings.AppsToSkip, app.Path])))).ToList();
        items.Add(("Browse\u2026", true, () =>
        {
            var dialog = new OpenFileDialog { Filter = "Apps (*.exe)|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                Save([.. _module.Settings.AppsToSkip, dialog.FileName]);
        }));
        return SettingsUi.Menu(_add, [.. items]);
    }

    /// <summary>Apps with a window, by name. One that can't be read (another user's, elevated) is left out.</summary>
    private static List<(string Name, string Path)> RunningApps()
    {
        var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == self || process.MainWindowHandle == 0 || process.MainModule?.FileName is not { } path)
                        continue;
                    apps.TryAdd(path, AppName(path));
                }
                catch (Exception)
                {
                    // Access denied or gone.
                }
            }
        }
        return [.. apps.Select(a => (a.Value, a.Key)).OrderBy(a => a.Value, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The app's own name for itself, else its file name.</summary>
    internal static string AppName(string path)
    {
        try
        {
            if (File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                return description.Trim();
        }
        catch (Exception)
        {
            // Unreadable version info.
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    private static Border Monogram(string name)
    {
        var letter = SettingsUi.Text(name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?", "CaptionTextStyle");
        letter.HorizontalAlignment = HorizontalAlignment.Center;
        var box = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 12, 0), Child = letter, VerticalAlignment = VerticalAlignment.Center };
        box.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        return box;
    }
}
