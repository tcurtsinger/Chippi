// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Island.Tests;

namespace Chippi.Features.CloseProtection.Tests;

public sealed class CloseProtectionUiTests
{
    [Fact]
    public void UiaToggleAndAppRemovalUseApprovedRowsWithTheRealSettingsTheme()
    {
        WpfTestThread.Run(() =>
        {
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") });
            bool light = WpfTestThread.ShotsTheme == "Light";
            Application.Current.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#6B4FBB" : "#B79CFF"));
            Application.Current.Resources["OnAccentBrush"] = light ? Brushes.White : Brushes.Black;
            var f = new CloseProtectionModuleTests.Fixture(true);
            f.Start().GetAwaiter().GetResult();
            var rows = (CloseProtectionRows)f.Ui.Page!(null);
            rows.Measure(new Size(768, double.PositiveInfinity));
            rows.Arrange(new Rect(0, 0, 768, rows.DesiredSize.Height));
            var toggle = Find<ToggleButton>(rows).Single();
            toggle.ApplyTemplate();
            var peer = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle);
            Assert.False(f.Module.Settings.Enabled);
            Assert.False(Find<Button>(rows).Single(b => Equals(b.Content, "Add app")).IsEnabled);
            Shot(rows, "off");
            peer.Toggle();
            Assert.True(f.Module.Settings.Enabled);
            Assert.Equal("On", toggle.Content);
            string path = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            Chippi.UI.Controls.AppIcon.LoadAsync(path, 80).GetAwaiter().GetResult();
            f.Module.AddApp(path);
            // Creating a page again is the normal Settings navigation path, with saved app data already filled.
            rows = (CloseProtectionRows)f.Ui.Page!(null);
            Assert.Single(Find<Image>(rows));
            Assert.Single(Find<Button>(rows), b => Equals(b.Content, "Remove"));
            Shot(rows, "on");
            Find<Button>(rows).Single(b => Equals(b.Content, "Remove")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Empty(f.Module.Settings.ControlWApps);
            f.Module.StopAsync(default).GetAwaiter().GetResult();
        });
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }
    private static void Shot(FrameworkElement rows, string state)
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        rows.Measure(new Size(768, double.PositiveInfinity));
        rows.Arrange(new Rect(0, 0, 768, rows.DesiredSize.Height));
        rows.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1536, (int)Math.Ceiling(rows.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(rows);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, "close-" + state + ".png"));
        png.Save(file);
    }
}
