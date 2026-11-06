// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Shortcuts;
using Chippi.Features.Displays.Views;
using Chippi.Island.Tests;

namespace Chippi.Features.Displays.Tests;

/// <summary>Settings › Displays over a fake model: rows, Try again, the keys' settings, Edit, and the shots.</summary>
public sealed class DisplaysPageTests
{
    [Fact]
    public void Each_display_shows_its_level_or_what_failed()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new DisplaysSettingsPage(model);
            Layout(page, 768);
            var rows = page.DisplayRows.ToList();
            Assert.Equal(["OMEN 34c", "LS27D36xG", "Dell P2419H"], rows.Select(r => r.Header));
            Assert.Equal("Main display · Brightness and contrast", rows[0].Description);
            Assert.Equal("Brightness and contrast", rows[1].Description);
            Assert.Equal(DisplaysSettingsPage.Causes, rows[2].Description);
            var texts = Texts(page);
            Assert.Contains("54%", texts);
            Assert.Contains("60%", texts);
            Assert.Contains(DisplaysSettingsPage.FailedText, texts);
            Assert.Single(texts, t => t == DisplaysSettingsPage.FailedText);

            var tryAgain = page.TryAgainOf("dell");
            Assert.Equal("Try again on Dell P2419H", AutomationProperties.GetName(tryAgain));
            tryAgain.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(["dell"], model.Retried);
        });
    }

    [Fact]
    public void A_display_that_answers_after_Try_again_shows_its_level_in_place()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new DisplaysSettingsPage(model);
            model.Set(model.Displays.Select(d => d.Id == "dell" ? d with { Retrying = true } : d).ToArray());
            Assert.False(page.TryAgainOf("dell").IsEnabled);
            model.Set(model.Displays.Select(d => d.Id == "dell" ? d with { Failed = false, Retrying = false, Brightness = 75, Contrast = false } : d).ToArray());
            var rows = page.DisplayRows.ToList();
            Assert.Equal("Dell P2419H", rows[2].Header);
            Assert.Equal("Brightness", rows[2].Description);
            Assert.Contains("75%", Texts(page));
            Assert.DoesNotContain(DisplaysSettingsPage.FailedText, Texts(page));
        });
    }

    [Fact]
    public void Displays_that_leave_drop_out_and_none_reads_No_displays_found()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new DisplaysSettingsPage(model);
            model.Set([model.Displays[1]]);
            Assert.Equal(["LS27D36xG"], page.DisplayRows.Select(r => r.Header));
            model.Set([]);
            Assert.Empty(page.DisplayRows);
            Assert.Contains("No displays found", Texts(page));
        });
    }

    [Fact]
    public void Act_on_and_Step_save_through_the_model_and_a_refusal_reads_Save_failed()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new DisplaysSettingsPage(model);
            Assert.Equal("Display under the pointer", page.ActOn.SelectedItem);
            Assert.Equal("10%", page.Step.SelectedItem);
            page.ActOn.SelectedIndex = 1;
            page.Step.SelectedIndex = 2;
            Assert.Equal(BrightnessTarget.AllDisplays, model.Settings.ActOn);
            Assert.Equal(20, model.Settings.Step);
            Assert.Equal(Visibility.Collapsed, page.KeysError.Visibility);

            model.Refuse = true;
            page.Step.SelectedIndex = 0;
            Assert.Equal("Save failed", page.KeysError.Text);
            Assert.Equal(Visibility.Visible, page.KeysError.Visibility);
            Assert.Equal("20%", page.Step.SelectedItem); // Back to what's saved.
        });
    }

    [Fact]
    public void Shortcuts_are_read_only_and_Edit_opens_the_slot_in_Settings_Shortcuts()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new DisplaysSettingsPage(model);
            Assert.Equal(new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x26).ToString(), page.ShortcutOf(DisplaysShortcuts.BrightnessUp).KeysText);
            Assert.Equal("Not set", page.ShortcutOf(DisplaysShortcuts.OpenDisplays).KeysText);
            var edit = page.ShortcutOf(DisplaysShortcuts.BrightnessDown).EditButton;
            Assert.Equal("Edit Brightness down shortcut", AutomationProperties.GetName(edit));
            edit.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal([DisplaysShortcuts.BrightnessDown], model.Edited);
        });
    }

    /// <summary>The page at the Settings window's full width (768) and its narrowest (860 window: 508), dark or light by CHIPPI_SHOTS_THEME.</summary>
    [Fact]
    public void Shots()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder) return;
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            Shot(new DisplaysSettingsPage(model), folder, "displays", 768);
            Shot(new DisplaysSettingsPage(model), folder, "displays-narrow", 508);
            model.Refuse = true;
            var refused = new DisplaysSettingsPage(model);
            refused.Step.SelectedIndex = 0;
            Shot(refused, folder, "displays-refused", 768);
        });
    }

    internal static void Theme()
    {
        if (Application.Current.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.EndsWith("SettingsStyles.xaml", StringComparison.Ordinal) == true))
            return;
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") });
        bool light = WpfTestThread.ShotsTheme == "Light";
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#6B4FBB" : "#B79CFF"));
        Application.Current.Resources["OnAccentBrush"] = light ? Brushes.White : Brushes.Black;
    }

    internal static List<string> Texts(DependencyObject root) => [.. Find<TextBlock>(root).Select(t => t.Text)];

    internal static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is UIElement { Visibility: not Visibility.Visible }) yield break;
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    internal static void Layout(FrameworkElement view, double width)
    {
        view.Measure(new Size(width, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
        view.UpdateLayout();
    }

    /// <summary>The page under its title on the window's background, at 2x.</summary>
    internal static void Shot(FrameworkElement page, string folder, string name, double width)
    {
        var title = new TextBlock { Text = "Displays", Margin = new Thickness(0, 0, 0, 12) };
        title.SetResourceReference(FrameworkElement.StyleProperty, "PageTitleStyle");
        var view = new Border { Padding = new Thickness(40, 24, 40, 32), Child = new StackPanel { Children = { title, page } } };
        view.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        Layout(view, width + 80);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * 2), (int)Math.Ceiling(view.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, $"{name}-{WpfTestThread.ShotsTheme.ToLowerInvariant()}.png"));
        png.Save(file);
    }

    private sealed class FakeModel : IDisplaysPageModel
    {
        private IReadOnlyList<DisplayStatus> _displays =
        [
            new("omen", "OMEN 34c", IsMain: true, Brightness: 54, Contrast: true),
            new("samsung", "LS27D36xG", IsMain: false, Brightness: 60, Contrast: true),
            new("dell", "Dell P2419H", IsMain: false, Brightness: null, Contrast: false, Failed: true),
        ];

        public DisplaysSettings Settings { get; private set; } = new();

        public IReadOnlyList<DisplayStatus> Displays => _displays;

        public string? SettingsError => null;

        public bool Refuse { get; set; }

        public List<string> Retried { get; } = [];

        public List<string> Edited { get; } = [];

        public event Action? Changed;

        public void Set(IReadOnlyList<DisplayStatus> displays)
        {
            _displays = displays;
            Changed?.Invoke();
        }

        public (KeyShortcut? Keys, string? Error) Shortcut(string name) => name == DisplaysShortcuts.BrightnessUp
            ? (new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x26), null)
            : (null, null);

        public bool SetActOn(BrightnessTarget target) => Save(Settings with { ActOn = target });

        public bool SetStep(int percent) => Save(Settings with { Step = percent });

        public void TryAgain(string displayId) => Retried.Add(displayId);

        public void EditShortcut(string name) => Edited.Add(name);

        private bool Save(DisplaysSettings settings)
        {
            if (Refuse) return false;
            Settings = settings;
            return true;
        }
    }
}
