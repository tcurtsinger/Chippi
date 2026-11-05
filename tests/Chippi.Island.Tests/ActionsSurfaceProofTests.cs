// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.App.Settings.Pages;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.QuickActions.Views;
using Chippi.Features.QuickActions;
using Chippi.Features.QuickActions.Tests;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

/// <summary>Opt-in native renders with no Window, Popup, native input or real Windows actions.</summary>
public sealed class ActionsSurfaceProofTests
{
    [Fact]
    public void Render_without_a_desktop_window()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ACTIONS_SHOTS") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        WpfTestThread.Run(() =>
        {
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") });
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") });
            var settings = new Store();
            var pc = new FakePc
            {
                State = QuickState.Empty with
                {
                    On = new Dictionary<QuickActionId, bool> { [QuickActionId.Dark] = true, [QuickActionId.WiFi] = true, [QuickActionId.Camera] = true },
                    Apps = [.. Enumerable.Range(1, 1000).Select(i => new OpenApp($@"C:\Apps\app{i}.exe", i == 1 ? "A very long app name · 日本語 · العربية · Éditeur" : $"Workbench {i}", i == 1))],
                    Plans = [new(Guid.NewGuid(), "Balanced", true), new(Guid.NewGuid(), "A very long power plan name for a workstation", false)],
                    Drives = [new("e", "Long portable archive and project backup (E:)", 1_000_000_000_000), new("f", "Archive (F:)", 64_000_000_000)],
                },
            };
            var module = new QuickActionsModule(() => pc, () => new Hotkey());
            module.StartAsync(new ModuleContext(QuickActionsModule.Id, settings, NullLogger.Instance, new IslandActivityHub().CreatePublisher(QuickActionsModule.Id), new Pages()), default).GetAwaiter().GetResult();
            try
            {
                module.Pick(QuickAction.All.Select(a => a.Id));
                var page = new ActionsPage(module);
                var root = Wrap(page);
                Save(root, Path.Combine(folder, "gallery.png"));
                module.Pick([QuickActionId.Dark, QuickActionId.Hidden, QuickActionId.Icons, QuickActionId.Bluetooth, QuickActionId.Lock, QuickActionId.Recycle]);
                page.BeginEdit();
                Save(root, Path.Combine(folder, "editor.png"));
                page.MoveEditor(0, 19);
                settings.Fail = true;
                page.FinishEdit();
                Save(root, Path.Combine(folder, "save-failed.png"));
                settings.Fail = false;
                page.FinishEdit();
                module.Pick(QuickAction.All.Select(a => a.Id));
                page.Render();
                page.Press(QuickActionId.Quit);
                Save(root, Path.Combine(folder, "quit-long-names.png"));
                page.Back();
                page.Press(QuickActionId.Eject);
                Save(root, Path.Combine(folder, "eject-long-names.png"));
                page.Back();
                var settingsPage = new QuickActionsSettingsPage(module);
                SaveSettings(settingsPage, "Quick actions", Path.Combine(folder, "settings.png"));
                settings.Fail = true;
                var visibility = Find<ToggleButton>(settingsPage).First();
                visibility.IsChecked = visibility.IsChecked != true;
                SaveSettings(settingsPage, "Quick actions", Path.Combine(folder, "settings-save-failed.png"));
                settings.Fail = false;
                var menu = settingsPage.AddMenu(new Button());
                Save((FrameworkElement)menu.Child, Path.Combine(folder, "kept-app-picker.png"));
                module.SaveLayout([QuickActionId.Bluetooth], module.Layout);
                pc.State = pc.State with { Unsupported = new HashSet<QuickActionId> { QuickActionId.Bluetooth } };
                module.RefreshAsync().GetAwaiter().GetResult();
                page.Render();
                Save(root, Path.Combine(folder, "unavailable.png"));
                module.Pick(QuickAction.All.Select(a => a.Id));
                page.Render();
                var samples = new List<double>();
                for (int i = 0; i < 120; i++)
                {
                    var timer = Stopwatch.StartNew();
                    page.Render();
                    root.UpdateLayout();
                    if (i >= 20) samples.Add(timer.Elapsed.TotalMilliseconds);
                }
                samples.Sort();
                File.WriteAllText(Path.Combine(folder, "timings.txt"), $"Cached gallery refresh + layout, 100 samples after 20 warmups: median {samples[50]:F3} ms, p95 {samples[95]:F3} ms.\nNative WPF controls rendered without a Window; excludes compositor, desktop input and OS calls.\n");
            }
            finally { module.StopAsync(default).GetAwaiter().GetResult(); }
        });
    }

    private static Border Wrap(ActionsPage page)
    {
        var header = new DockPanel();
        var back = Ui.Icon(LucideIcons.ChevronLeft, "Back");
        back.Margin = new Thickness(0, 0, 8, 0);
        header.Children.Add(back);
        header.Children.Add(page.Header);
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(IslandPage.HeaderHeight) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(IslandPage.Gap) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(IslandPage.BodyHeight) });
        body.Children.Add(header);
        Grid.SetRow(page, 2);
        body.Children.Add(page);
        var root = new Border { Width = IslandPage.Width, Height = IslandPage.Height, Padding = new Thickness(IslandPage.Inset), CornerRadius = new CornerRadius(8), Child = body };
        root.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        return root;
    }

    private static void Save(FrameworkElement root, string path)
    {
        root.Measure(new Size(double.IsNaN(root.Width) ? root.MaxWidth : root.Width, double.IsNaN(root.Height) ? double.PositiveInfinity : root.Height));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 1.5), (int)Math.Ceiling(root.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void SaveSettings(FrameworkElement view, string title, string path)
    {
        var root = new Border { Width = 824, Padding = new Thickness(32, 24, 32, 32), Child = new ModulePageHost(title, view) };
        root.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        Save(root, path);
        if (view.Parent is Panel parent) parent.Children.Remove(view);
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var match in Find<T>(child)) yield return match;
    }

    private sealed class Store : IModuleSettings
    {
        private QuickActionsSettings _value = new();
        internal bool Fail;
        public T Get<T>() where T : class, new() => (T)(object)_value;
        public void Save<T>(T value) where T : class
        {
            if (Fail) throw new IOException("Fixture write failure");
            _value = (QuickActionsSettings)(object)value;
        }
    }
    private sealed class Pages : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(QuickActionsModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> factory) { }
    }
    private sealed class Hotkey : IHotkey
    {
        public bool Register(KeyShortcut shortcut, Action pressed) => true;
        public void Dispose() { }
    }
}
