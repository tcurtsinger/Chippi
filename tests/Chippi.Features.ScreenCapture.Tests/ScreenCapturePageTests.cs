// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Shortcuts;
using Chippi.Features.ScreenCapture.Views;
using Chippi.Island.Tests;

namespace Chippi.Features.ScreenCapture.Tests;

/// <summary>Settings › Screen capture over a fake model with 50 synthetic captures: every row, the states, Edit, and the shots.</summary>
public sealed class ScreenCapturePageTests
{
    private static readonly string Pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    [Fact]
    public void After_a_capture_saves_through_the_model_and_a_refusal_reads_Save_failed()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new ScreenCaptureSettingsPage(model);
            Assert.Equal("Copy", page.Enter.SelectedItem);
            Assert.Equal("PNG", page.FileType.SelectedItem);
            page.Enter.SelectedIndex = 2;
            page.FileType.SelectedIndex = 1;
            Assert.Equal(EnterAction.CopyAndSave, model.Settings.Enter);
            Assert.Equal(CaptureFileType.Jpeg, model.Settings.FileType);
            Assert.Equal(Visibility.Collapsed, page.AfterError.Visibility);

            model.Refuse = true;
            page.Enter.SelectedIndex = 0;
            Assert.Equal("Save failed", page.AfterError.Text);
            Assert.Equal(Visibility.Visible, page.AfterError.Visibility);
            Assert.Equal("Copy and save", page.Enter.SelectedItem); // Back to what's saved.
        });
    }

    [Fact]
    public void Save_folder_shows_Pictures_Screenshots_Change_picks_and_Reset_goes_back()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new ScreenCaptureSettingsPage(model);
            Assert.Equal(@"Pictures\Screenshots", page.Folder.Text);
            Assert.Equal(Visibility.Collapsed, page.ResetFolder.Visibility);
            Assert.Equal(Visibility.Collapsed, page.FolderMissing.Visibility);

            page.PickFolder = _ => null; // Cancel keeps the folder.
            Click(page.ChangeFolder);
            Assert.Null(model.Settings.SaveFolder);

            string chosen = Path.Combine(Pictures, "Shots", "Work");
            page.PickFolder = current => { Assert.Equal(CaptureFiles.DefaultFolder, current); return chosen; };
            Click(page.ChangeFolder);
            Assert.Equal(chosen, model.Settings.SaveFolder);
            Assert.Equal(@"Pictures\Shots\Work", page.Folder.Text);
            Assert.Equal(Visibility.Visible, page.ResetFolder.Visibility);

            model.Missing = true;
            model.Raise();
            Assert.Equal(Visibility.Visible, page.FolderMissing.Visibility);
            Assert.Equal("Folder not found", page.FolderMissing.Text);

            Click(page.ResetFolder);
            Assert.Null(model.Settings.SaveFolder);
            Assert.Equal(Visibility.Collapsed, page.ResetFolder.Visibility);
            Assert.Equal(@"D:\Shots", ScreenCaptureSettingsPage.Display(@"D:\Shots\"));
        });
    }

    [Fact]
    public void Recent_shows_every_capture_with_its_count_and_File_missing()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new ScreenCaptureSettingsPage(model);
            Layout(page, 768);
            Assert.Equal("50 captures", page.Count.Header);
            Assert.Equal(50, page.Tiles.Count);
            Assert.Equal(6, page.Grid.Columns);
            var first = model.Recent[0];
            Assert.StartsWith("Capture, 1040 × 800, ", AutomationProperties.GetName(page.Tiles[0]));
            Assert.EndsWith(", file missing", AutomationProperties.GetName(page.Tiles[1])); // Saved, then moved.
            Assert.Single(Texts(page), t => t == "File missing");

            model.SetRecent(model.Recent.Take(1).ToArray());
            Assert.Equal("1 capture", page.Count.Header);
            Assert.Single(page.Tiles);

            model.SetRecent([]);
            Assert.Equal(Visibility.Collapsed, page.Count.Visibility);
            Assert.Contains("No recent captures", Texts(page));

            Layout(page, 508);
            model.SetRecent(FakeModel.Captures(12));
            Layout(page, 508);
            Assert.Equal(4, page.Grid.Columns);
            Assert.True(page.Tiles[0].ActualWidth >= 100, $"tile {page.Tiles[0].ActualWidth} wide");
        });
    }

    [Fact]
    public void History_off_hides_an_empty_list_and_keeps_captures_that_can_still_be_cleared()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new ScreenCaptureSettingsPage(model);
            page.History.IsChecked = false;
            Assert.False(model.Settings.History);
            Assert.Equal("Off", page.History.Content);
            Assert.Equal(Visibility.Visible, page.Count.Visibility);
            model.SetRecent([]);
            Assert.DoesNotContain("No recent captures", Texts(page));
            page.History.IsChecked = true;
            Assert.Contains("No recent captures", Texts(page));
        });
    }

    [Fact]
    public void A_capture_menu_has_Annotate_Remove_and_Delete_only_for_a_saved_file_still_there()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new ScreenCaptureSettingsPage(model);
            var saved = model.Recent[0];
            var missing = model.Recent[1];
            var copied = model.Recent[2];
            Assert.Equal(["Annotate", "Remove from recent", "Delete file"], Headers(page.MenuOf(saved.Id)));
            Assert.Equal(["Annotate", "Remove from recent"], Headers(page.MenuOf(missing.Id)));
            Assert.Equal(["Annotate", "Remove from recent"], Headers(page.MenuOf(copied.Id)));

            Click(page.MenuOf(copied.Id).Items.OfType<MenuItem>().First());
            Assert.Equal([copied.Id], model.Annotated);
            Assert.Equal(Visibility.Collapsed, page.RecentError.Visibility);
            model.AnnotateWorks = false;
            Click(page.MenuOf(copied.Id).Items.OfType<MenuItem>().First());
            Assert.Equal("Annotate failed", page.RecentError.Text);
            Assert.Equal(Visibility.Visible, page.RecentError.Visibility);
            Click(page.MenuOf(copied.Id).Items.OfType<MenuItem>().ElementAt(1));
            Assert.Equal(49, page.Tiles.Count);
        });
    }

    [Fact]
    public void Shortcuts_are_read_only_Capture_region_reads_Used_by_Windows_and_Edit_opens_Settings_Shortcuts()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var page = new ScreenCaptureSettingsPage(model);
            Assert.Equal(new KeyShortcut((ShortcutModifiers)0, 0x2C).ToString(), page.ShortcutOf(ScreenCaptureShortcuts.CaptureRegion).KeysText);
            Assert.Equal("Not set", page.ShortcutOf(ScreenCaptureShortcuts.OpenCapture).KeysText);
            model.WindowsHasPrintScreen = true;
            model.Raise();
            Assert.Equal("Used by Windows", page.ShortcutOf(ScreenCaptureShortcuts.CaptureRegion).KeysText);
            Assert.Single(Texts(page), t => t == "Used by Windows"); // In place of the keys, not under them.

            var edit = page.ShortcutOf(ScreenCaptureShortcuts.OpenCapture).EditButton;
            Assert.Equal("Edit Open Capture shortcut", AutomationProperties.GetName(edit));
            Click(edit);
            Assert.Equal([ScreenCaptureShortcuts.OpenCapture], model.Edited);
        });
    }

    /// <summary>Building the page with 50 thumbnails and laying it out, as Settings does when it opens the page; CHIPPI_CAPTURE_SETTINGS_TIMINGS names a report.</summary>
    [Fact]
    public void Opens_with_50_captures_in_well_under_a_frame_budget()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            var times = new List<double>();
            for (int i = 0; i < 11; i++)
            {
                var clock = Stopwatch.StartNew();
                var page = new ScreenCaptureSettingsPage(model);
                Layout(page, 768);
                times.Add(clock.Elapsed.TotalMilliseconds);
            }
            if (Environment.GetEnvironmentVariable("CHIPPI_CAPTURE_SETTINGS_TIMINGS") is { Length: > 0 } report)
                File.WriteAllLines(report, times.Select((t, i) => $"{(i == 0 ? "first" : $"open {i}")}: {t:F1} ms"));
            // The median, not the slowest: one open on CI's shared runner took 116 ms while the rest were fast.
            var warm = times.Skip(1).Order().ToList();
            var median = (warm[(warm.Count - 1) / 2] + warm[warm.Count / 2]) / 2;
            Assert.True(median < 100, $"median warm open {median:F1} ms (slowest {warm[^1]:F1} ms)");
        });
    }

    /// <summary>The page at the Settings window's full width (768) and its narrowest (508), dark or light by CHIPPI_SHOTS_THEME.</summary>
    [Fact]
    public void Shots()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder) return;
        WpfTestThread.Run(() =>
        {
            Theme();
            var model = new FakeModel();
            Shot(new ScreenCaptureSettingsPage(model), folder, "screen-capture", 768);
            Shot(new ScreenCaptureSettingsPage(model), folder, "screen-capture-narrow", 508);
            var states = new FakeModel { WindowsHasPrintScreen = true, Missing = true };
            states.SetFolder(@"E:\Captures");
            states.SetRecent([]);
            Shot(new ScreenCaptureSettingsPage(states), folder, "screen-capture-states", 768);
            var refused = new FakeModel { Refuse = true };
            var page = new ScreenCaptureSettingsPage(refused);
            page.Magnifier.IsChecked = false;
            Shot(page, folder, "screen-capture-refused", 508);
        });
    }

    private static List<string> Headers(ContextMenu menu) => [.. menu.Items.OfType<MenuItem>().Select(i => (string)i.Header)];

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

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
    internal static void Shot(FrameworkElement page, string folder, string name, double width, string title = "Screen capture")
    {
        var heading = new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 12) };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "PageTitleStyle");
        var view = new Border { Padding = new Thickness(40, 24, 40, 32), Child = new StackPanel { Children = { heading, page } } };
        view.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        Layout(view, width + 80);
        Layout(view, width + 80); // The grid picks its columns from its first width.
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * 2), (int)Math.Ceiling(view.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, $"{name}-{WpfTestThread.ShotsTheme.ToLowerInvariant()}.png"));
        png.Save(file);
    }

    private sealed class FakeModel : IScreenCapturePageModel
    {
        private static readonly Color[] Paper = [Color.FromRgb(0xF4, 0xF1, 0xEA), Color.FromRgb(0x1E, 0x1F, 0x26), Color.FromRgb(0x3A, 0x6E, 0xA5), Color.FromRgb(0xFF, 0xFF, 0xFF)];
        private readonly Dictionary<Guid, ImageSource> _thumbnails = [];
        private IReadOnlyList<RecentCapture> _recent = Captures(50);

        public ScreenCaptureSettings Settings { get; private set; } = new();

        public string SaveFolder => Settings.SaveFolder ?? CaptureFiles.DefaultFolder;

        public bool SaveFolderMissing => Missing && Settings.SaveFolder is not null;

        public string? SettingsError => null;

        public IReadOnlyList<RecentCapture> Recent => _recent;

        public bool Refuse { get; set; }

        public bool Missing { get; set; }

        public bool WindowsHasPrintScreen { get; set; }

        public List<string> Edited { get; } = [];

        public List<Guid> Annotated { get; } = [];

        public event Action? Changed;

        /// <summary>Synthetic captures, newest first: saved, saved then moved, copied only, repeating.</summary>
        public static IReadOnlyList<RecentCapture> Captures(int count)
        {
            var now = DateTimeOffset.Now;
            return [.. Enumerable.Range(0, count).Select(i => new RecentCapture(
                new Guid(i + 1, 0, 0, new byte[8]), $"{i}.png", now.AddMinutes(-37 * i), 1040 - i % 5 * 120, 800 - i % 3 * 90,
                (i % 3) switch { 0 => Path.Combine(CaptureFiles.DefaultFolder, $"Capture {i}.png"), 1 => i == 1 ? @"Z:\gone\Capture 1.png" : Path.Combine(CaptureFiles.DefaultFolder, $"Capture {i}.png"), _ => null }))];
        }

        public void Raise() => Changed?.Invoke();

        public void SetRecent(IReadOnlyList<RecentCapture> recent)
        {
            _recent = recent;
            Changed?.Invoke();
        }

        public void SetFolder(string folder) => Settings = Settings with { SaveFolder = folder };

        public ImageSource? Thumbnail(Guid id)
        {
            if (_thumbnails.TryGetValue(id, out var image)) return image;
            int n = id.ToByteArray()[0];
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var paper = Paper[n % Paper.Length];
                dc.DrawRectangle(new SolidColorBrush(paper), null, new Rect(0, 0, 208, 160));
                var ink = new SolidColorBrush(paper.R + paper.G + paper.B > 384 ? Color.FromRgb(0x50, 0x52, 0x5C) : Color.FromRgb(0xC8, 0xCA, 0xD2));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x7C, 0x5C, 0xFF)), null, new Rect(0, 0, 208, 18));
                for (int line = 0; line < 6; line++)
                    dc.DrawRectangle(ink, null, new Rect(14, 32 + line * 18, 60 + (n * 37 + line * 53) % 120, 7));
            }
            var bitmap = new RenderTargetBitmap(208, 160, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return _thumbnails[id] = bitmap;
        }

        public bool FileMissing(RecentCapture capture) => capture.SavedFile?.StartsWith(@"Z:\", StringComparison.Ordinal) == true;

        public (KeyShortcut? Keys, string? Error) Shortcut(string name) => name == ScreenCaptureShortcuts.CaptureRegion
            ? (new KeyShortcut((ShortcutModifiers)0, 0x2C), WindowsHasPrintScreen ? "Used by Windows" : null)
            : (null, null);

        public bool SetEnter(EnterAction action) => Save(Settings with { Enter = action });

        public bool SetSaveFolder(string? folder) => Save(Settings with { SaveFolder = folder });

        public bool SetFileType(CaptureFileType type) => Save(Settings with { FileType = type });

        public bool SetHistory(bool on) => Save(Settings with { History = on });

        public bool SetCtrlDragCopies(bool on) => Save(Settings with { CtrlDragCopies = on });

        public bool SetMagnifier(bool on) => Save(Settings with { Magnifier = on });

        public bool AnnotateWorks { get; set; } = true;

        public bool Annotate(Guid id)
        {
            Annotated.Add(id);
            return AnnotateWorks;
        }

        public void Remove(Guid id) => SetRecent([.. _recent.Where(c => c.Id != id)]);

        public bool DeleteFile(Guid id) => !Refuse;

        public void ClearRecent() => SetRecent([]);

        public void EditShortcut(string name) => Edited.Add(name);

        private bool Save(ScreenCaptureSettings settings)
        {
            if (Refuse) return false;
            Settings = settings;
            Changed?.Invoke();
            return true;
        }
    }
}
