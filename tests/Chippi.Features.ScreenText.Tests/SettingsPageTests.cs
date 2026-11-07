// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Shortcuts;
using Chippi.Features.ScreenText.Views;
using Chippi.Island.Tests;

namespace Chippi.Features.ScreenText.Tests;

/// <summary>Settings › Text and QR over a fake model: Language, Keep line breaks, no OCR language, Edit, and the shots.</summary>
public sealed class SettingsPageTests
{
    [Fact]
    public void Language_lists_the_installed_ones_and_saves_through_the_model()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new ScreenTextSettingsPage(model);
            Assert.Equal(["English (United States)", "Deutsch (Deutschland)", "日本語"], page.LanguagePicker.Items.Cast<string>());
            Assert.Equal("English (United States)", page.LanguagePicker.SelectedItem);
            page.LanguagePicker.SelectedIndex = 1;
            Assert.Equal("de-DE", model.Language);

            model.Refuse = true;
            page.LanguagePicker.SelectedIndex = 2;
            Assert.Equal("Save failed", page.Error.Text);
            Assert.Equal("Deutsch (Deutschland)", page.LanguagePicker.SelectedItem); // Back to what's saved.
        });
    }

    [Fact]
    public void Keep_line_breaks_flips_through_the_model()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new ScreenTextSettingsPage(model);
            Assert.Equal("On", page.KeepLines.Content);
            page.KeepLines.IsChecked = false;
            Assert.False(model.KeepLineBreaks);
            Assert.Equal("Off", page.KeepLines.Content);
        });
    }

    [Fact]
    public void With_no_OCR_language_the_row_says_so_and_opens_Windows_Settings()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel { None = true };
            var page = new ScreenTextSettingsPage(model);
            Assert.Equal(ScreenTextSettingsPage.NoLanguage, page.LanguageRow.Description);
            Assert.Same(page.OpenSettings, page.LanguageRow.Content);
            page.OpenSettings.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(1, model.Opened);

            model.None = false;
            model.Raise();
            Assert.Same(page.LanguagePicker, page.LanguageRow.Content);
            Assert.Equal("", page.LanguageRow.Description);
        });
    }

    [Fact]
    public void Shortcuts_are_read_only_and_Edit_opens_Settings_Shortcuts()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new ScreenTextSettingsPage(model);
            Assert.Equal(["Copy text from screen", "Extract text", "Scan QR", "Open Capture"], ScreenTextShortcuts.All);
            Assert.Equal("Not set", page.ShortcutOf(ScreenTextShortcuts.ScanQrCode).KeysText);
            var edit = page.ShortcutOf(ScreenTextShortcuts.ExtractText).EditButton;
            Assert.Equal("Edit Extract text shortcut", AutomationProperties.GetName(edit));
            edit.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal([ScreenTextShortcuts.ExtractText], model.Edited);
        });
    }

    [Fact]
    public void Shots()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder) return;
        WpfTestThread.Run(() =>
        {
            Theme();
            Shot(new ScreenTextSettingsPage(new FakeModel()), folder, "text-and-qr", 768);
            Shot(new ScreenTextSettingsPage(new FakeModel()), folder, "text-and-qr-narrow", 508);
            Shot(new ScreenTextSettingsPage(new FakeModel { None = true }), folder, "text-and-qr-no-language", 508);
        });
    }

    private static void Theme()
    {
        if (Application.Current.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.EndsWith("SettingsStyles.xaml", StringComparison.Ordinal) == true))
            return;
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") });
        bool light = WpfTestThread.ShotsTheme == "Light";
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#6B4FBB" : "#B79CFF"));
        Application.Current.Resources["OnAccentBrush"] = light ? Brushes.White : Brushes.Black;
    }

    private static void Layout(FrameworkElement view, double width)
    {
        view.Measure(new Size(width, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
        view.UpdateLayout();
    }

    /// <summary>The page under its title on the window's background, at 2x.</summary>
    private static void Shot(FrameworkElement page, string folder, string name, double width)
    {
        var heading = new TextBlock { Text = "Text and QR", Margin = new Thickness(0, 0, 0, 12) };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "PageTitleStyle");
        var view = new Border { Padding = new Thickness(40, 24, 40, 32), Child = new StackPanel { Children = { heading, page } } };
        view.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        Layout(view, width + 80);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * 2), (int)Math.Ceiling(view.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, $"settings-{name}-{WpfTestThread.ShotsTheme.ToLowerInvariant()}.png"));
        png.Save(file);
    }

    private sealed class FakeModel : IScreenTextSettingsModel
    {
        private static readonly IReadOnlyList<OcrLanguage> Installed =
            [new("en-US", "English (United States)"), new("de-DE", "Deutsch (Deutschland)"), new("ja", "日本語")];

        public bool None { get; set; }

        public bool Refuse { get; set; }

        public int Opened { get; private set; }

        public List<string> Edited { get; } = [];

        public IReadOnlyList<OcrLanguage> Languages => None ? [] : Installed;

        public string? Language { get; private set; } = "en-US";

        public bool KeepLineBreaks { get; private set; } = true;

        public string? SettingsError => null;

        public event Action? Changed;

        public void Raise() => Changed?.Invoke();

        public (KeyShortcut? Keys, string? Error) Shortcut(string name) => (null, null);

        public bool SetLanguage(string tag)
        {
            if (Refuse) return false;
            Language = tag;
            return true;
        }

        public bool SetKeepLineBreaks(bool on)
        {
            if (Refuse) return false;
            KeepLineBreaks = on;
            return true;
        }

        public void OpenLanguageSettings() => Opened++;

        public void EditShortcut(string name) => Edited.Add(name);
    }
}
