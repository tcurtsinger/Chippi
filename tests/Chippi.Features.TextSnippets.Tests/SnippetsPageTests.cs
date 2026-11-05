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
using Chippi.Features.TextSnippets.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;

namespace Chippi.Features.TextSnippets.Tests;

/// <summary>Settings › Text snippets against the approved boards (wave-3.html: snippets, snippets-empty, sheet-snippets-states).</summary>
public sealed class SnippetsPageTests
{
    private static Snippet S(string folder, string name, string trigger, string text, ExpandMode expand = ExpandMode.AfterDelimiter, bool enabled = true) =>
        new() { Folder = folder, Name = name, Trigger = trigger, Text = text, Expand = expand, Enabled = enabled };

    /// <summary>The board's twelve snippets in four folders, one off.</summary>
    internal static Snippet[] Board() =>
    [
        S("Email", "Signature", ";sig", "Sam Rivera\nPlatform team"),
        S("Email", "Email address", ";email", "sam.rivera@example.com", ExpandMode.RightAway),
        S("Email", "Work email", ";email2", "srivera@example.org", ExpandMode.RightAway),
        S("Email", "Out of office", ";ooo", "I'm out until {{date:dddd}} and will reply when I'm back."),
        S("Dates", "Today", ";today", "{{date:yyyy-MM-dd}}", ExpandMode.RightAway),
        S("Dates", "Timestamp", ";ts", "{{datetime:yyyy-MM-dd HH:mm}}", ExpandMode.RightAway),
        S("Dates", "New York time", ";nyt", "{{date-tz(America/New_York):h:mm tt}} ET", ExpandMode.RightAway),
        S("ServiceNow", "Story note", ";story", "Story {{clipboard}}:"),
        S("ServiceNow", "Close notes", ";close", "Resolved. Root cause and fix are in the work notes."),
        S("ServiceNow", "Script header", ";hdr", "/* Author: Sam Rivera · {{date}} */", enabled: false),
        S("Replies", "Thanks", ";ty", "Thanks, I appreciate it."),
        S("Replies", "On it", ";oi", "On it, I'll update you by end of day."),
    ];

    private sealed class Fixture
    {
        public readonly ModuleTests.Hub Hub = new();
        public readonly ModuleTests.Store Store = new();
        public readonly ModuleTests.Ui Ui = new();
        public readonly TextSnippetsModule Module = new(new ModuleTests.Sender(), new ModuleTests.Reader(), () => false);

        public SnippetsPage Start(TextSnippetsSettings settings)
        {
            Store.Value = settings;
            Module.StartAsync(ModuleTests.Context(Hub, Store, Ui), default).GetAwaiter().GetResult();
            return (SnippetsPage)Ui.Page!(null);
        }
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

    [Fact]
    public void TheListGroupsByFolderAndSearchFiltersIt()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var f = new Fixture();
            string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            AppIcon.LoadAsync(notepad, 80).GetAwaiter().GetResult();
            var page = f.Start(new TextSnippetsSettings { ExpandWhileTyping = true, Snippets = Board(), IgnoredApps = [notepad] });
            Layout(page, 768);
            Assert.Equal(["Email", "Dates", "ServiceNow", "Replies"], Texts(page).Where(t => t is "Email" or "Dates" or "ServiceNow" or "Replies").ToArray());
            Assert.Contains("12 snippets", Texts(page));
            Assert.Contains("After Space, Tab or Enter", Texts(page));
            Assert.Contains("Sam Rivera · Platform team", Texts(page));
            Shot(page, "snippets", 768);
            Shot(page, "snippets-narrow", 620);

            var search = Find<SearchBox>(page).Single();
            search.Text = "email";
            Assert.Contains("4 of 12", Texts(page));
            Assert.DoesNotContain("Thanks", Texts(page));
            Shot(page, "snippets-search", 768);
            search.Text = "zoom";
            Assert.Contains("0 of 12", Texts(page));
            Assert.Contains("No results", Texts(page));
            Shot(page, "snippets-no-results", 768);
            search.Text = "";

            // A row's switch, through UI Automation as Narrator would: it raises Checked and Unchecked, not Click.
            var toggle = Find<ToggleButton>(page).First(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Thanks");
            toggle.ApplyTemplate();
            ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle)).Toggle();
            Assert.False(f.Module.Settings.Snippets.Single(s => s.Trigger == ";ty").Enabled);
            f.Module.StopAsync(default).GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void TheEditorRefusesWhatTheEngineCantSaveAndSavesTheRest()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var f = new Fixture();
            var page = f.Start(new TextSnippetsSettings { Snippets = Board() });
            Find<Button>(page).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Add snippet").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Layout(page, 768);
            Assert.Contains("New snippet", Texts(page));
            var save = Find<Button>(page).Single(b => Equals(b.Content, "Save"));
            Assert.False(save.IsEnabled);
            Assert.DoesNotContain(Texts(page), t => t.StartsWith("The trigger", StringComparison.Ordinal));
            Shot(page, "editor-new", 768, crop: true);

            Field(page, "Name").Text = "Meeting notes";
            Field(page, "Text").Text = "Notes · {{date:MMM d}}\nAttendees:\nNext steps:";
            Field(page, "Trigger").Text = ";";
            Assert.Contains("The trigger needs at least 2 characters", Texts(page));
            Assert.False(save.IsEnabled);
            Shot(page, "editor-too-short", 768, crop: true);

            Field(page, "Trigger").Text = ";sig";
            Assert.Contains("Another snippet already uses this trigger", Texts(page));
            Assert.False(save.IsEnabled);
            Shot(page, "editor-duplicate", 768, crop: true);

            Field(page, "Trigger").Text = ";e";
            Assert.Contains("This trigger starts another one", Texts(page));
            Assert.True(save.IsEnabled);
            Shot(page, "editor-starts-another", 768, crop: true);

            Field(page, "Trigger").Text = ";mn";
            var folder = Find<ComboBox>(page).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Folder");
            Assert.Equal("Replies", folder.SelectedItem);
            folder.SelectedItem = "New folder…";
            Assert.Equal(Visibility.Collapsed, folder.Visibility);
            Field(page, "Folder name").Text = "Meetings";
            save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var saved = f.Module.Settings.Snippets[^1];
            Assert.Equal(("Meeting notes", ";mn", "Meetings"), (saved.Name, saved.Trigger, saved.Folder));
            Assert.Equal(13, f.Module.Settings.Snippets.Length);
            Assert.Contains("13 snippets", Texts(page));

            // Edit opens in the row's place; Cancel keeps the saved snippet as it was.
            var story = f.Module.Settings.Snippets.Single(s => s.Trigger == ";story");
            page.StartEdit(SnippetsPage.From(story));
            Layout(page, 768);
            Assert.Contains("Edit snippet", Texts(page));
            Assert.DoesNotContain("Story note", Find<TextBlock>(page).Select(t => t.Text));
            Shot(page, "editor-edit", 768);
            Field(page, "Name").Text = "Changed";
            Find<Button>(page).Single(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("Story note", f.Module.Settings.Snippets.Single(s => s.Trigger == ";story").Name);
            Assert.Contains("Story note", Texts(page));
            f.Module.StopAsync(default).GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void FirstInstallAndAHookThatFailedHaveTheirOwnRows()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var f = new Fixture();
            var page = f.Start(new TextSnippetsSettings());
            Layout(page, 768);
            Assert.Contains("No snippets yet", Texts(page));
            Assert.Contains("Off", Find<ToggleButton>(page).Select(t => t.Content as string));
            Shot(page, "snippets-empty", 768);
            f.Module.StopAsync(default).GetAwaiter().GetResult();

            var down = new Fixture();
            down.Hub.Fails = true;
            page = down.Start(new TextSnippetsSettings { ExpandWhileTyping = true, Snippets = Board()[..2] });
            Layout(page, 768);
            Assert.Contains("Snippet expansion failed", Texts(page));
            down.Hub.Fails = false;
            Find<Button>(page).Single(b => Equals(b.Content, "Retry")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Null(down.Module.Error);
            Assert.Single(down.Hub.Filters);
            down.Module.StopAsync(default).GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void TheDateAndTimePickerAndTheDeleteQuestion()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            Assert.Equal("{{date}}", DateTimePicker.Token(DateTimePicker.Kind.Date, "d", null));
            Assert.Equal("{{date:ddd, MMM d}}", DateTimePicker.Token(DateTimePicker.Kind.Date, "ddd, MMM d", null));
            Assert.Equal("{{time-tz(America/New_York):HH:mm}}", DateTimePicker.Token(DateTimePicker.Kind.Time, "HH:mm", "America/New_York"));
            Assert.Null(DateTimePicker.Preview("}", DateTimeOffset.Now));
            Assert.Null(DateTimePicker.Preview("", DateTimeOffset.Now));

            string? inserted = null;
            var popup = DateTimePicker.Create(new Button(), t => inserted = t);
            var panel = (FrameworkElement)popup.Child;
            Layout(panel, double.PositiveInfinity);
            Field(panel, "Pattern").Text = "ddd, MMM d";
            Assert.Contains(DateTimeOffset.Now.ToString("ddd, MMM d", System.Globalization.CultureInfo.CurrentCulture), Texts(panel));
            Shot(panel, "date-picker", double.NaN);
            Find<Button>(panel).Single(b => Equals(b.Content, "Insert")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("{{date:ddd, MMM d}}", inserted);

            bool deleted = false;
            var question = SnippetsPage.ConfirmPanel(() => { }, () => deleted = true, out _);
            Layout(question, double.PositiveInfinity);
            Assert.Contains("Delete this snippet?", Texts(question));
            Shot(question, "delete-question", double.NaN);
            Find<Button>(question).Single(b => Equals(b.Content, "Delete")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(deleted);
        });
    }

    private static TextBox Field(DependencyObject root, string name) =>
        Find<TextBox>(root).First(t => System.Windows.Automation.AutomationProperties.GetName(t) == name);

    private static List<string> Texts(DependencyObject root) => [.. Find<TextBlock>(root).Select(t => t.Text)];

    /// <summary>What's showing: a collapsed element (a row search hid) and what's in it are left out.</summary>
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is UIElement { Visibility: not Visibility.Visible }) yield break;
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    private static void Layout(FrameworkElement view, double width)
    {
        view.Measure(new Size(width, double.PositiveInfinity));
        double w = double.IsInfinity(width) ? view.DesiredSize.Width : width;
        view.Arrange(new Rect(0, 0, w, view.DesiredSize.Height));
        view.UpdateLayout();
    }

    /// <summary>The view at 2x on the window's background, when CHIPPI_ISLAND_SHOTS names a folder; NaN for its own width.</summary>
    private static void Shot(FrameworkElement view, string name, double width, bool crop = false)
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        Layout(view, double.IsNaN(width) ? double.PositiveInfinity : width);
        double w = view.ActualWidth, h = view.ActualHeight;
        if (crop) h = Math.Min(h, 760);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle((Brush)Application.Current.Resources["WindowBackgroundBrush"], null, new Rect(0, 0, w + 32, h + 32));
            dc.DrawRectangle(new VisualBrush(view) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h) }, null, new Rect(16, 16, w, h));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling((w + 32) * 2), (int)Math.Ceiling((h + 32) * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, $"snip-{name}-{WpfTestThread.ShotsTheme.ToLowerInvariant()}.png"));
        png.Save(file);
    }
}
