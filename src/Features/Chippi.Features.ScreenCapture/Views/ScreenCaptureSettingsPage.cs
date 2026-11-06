// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.ScreenCapture.Views;

/// <summary>
/// Settings › Screen capture (Settings › Capture v4, screen-capture.md Settings): After a capture (Enter key, Save folder,
/// File type), Recent captures (History, the count with Clear, every capture's thumbnail with Annotate, Remove from
/// recent and Delete file), Selecting (Ctrl+drag to copy, Magnifier), then Shortcuts, read-only with Edit, since they're
/// set in Settings › Shortcuts. Open Capture is the Capture host's slot, shown here too.
/// </summary>
public sealed class ScreenCaptureSettingsPage : UserControl
{
    internal const string UsedByWindows = "Used by Windows";
    internal const string FolderNotFound = "Folder not found";

    internal static readonly IReadOnlyList<(EnterAction Action, string Name)> EnterActions =
    [
        (EnterAction.Copy, "Copy"),
        (EnterAction.Save, "Save"),
        (EnterAction.CopyAndSave, "Copy and save"),
    ];

    internal static readonly IReadOnlyList<(CaptureFileType Type, string Name)> FileTypes =
    [
        (CaptureFileType.Png, "PNG"),
        (CaptureFileType.Jpeg, "JPEG"),
    ];

    private readonly IScreenCapturePageModel _model;
    private readonly ComboBox _enter = SettingsLayout.Combo(EnterActions.Select(a => (object)a.Name));
    private readonly ComboBox _fileType = SettingsLayout.Combo(FileTypes.Select(t => (object)t.Name));
    private readonly TextBlock _folder = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _folderMissing = new() { Text = FolderNotFound, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Button _change = Button("Change");
    private readonly Button _reset = Button("Reset");
    private readonly ToggleButton _history = Switch("History");
    private readonly ToggleButton _ctrlDrag = Switch("Ctrl+drag to copy");
    private readonly ToggleButton _magnifier = Switch("Magnifier");
    private readonly SettingRow _count = SettingsLayout.Row("");
    private readonly Button _clear = Button("Clear");
    private readonly UniformGrid _grid = new() { Columns = 6 };
    private readonly Border _gridBox;
    private readonly Border _none = SettingsLayout.Empty("No recent captures");
    private readonly Card _recent;
    private readonly Dictionary<Guid, Tile> _tiles = [];
    private readonly Dictionary<string, ShortcutSetting> _shortcuts = [];
    private readonly TextBlock _afterError = Error(), _recentError = Error(), _selectingError = Error();
    private bool _updating, _listening;

    public ScreenCaptureSettingsPage(IScreenCapturePageModel model)
    {
        _model = model;
        _enter.Width = 220;
        _fileType.Width = 180;
        AutomationProperties.SetName(_enter, "Enter key");
        AutomationProperties.SetName(_fileType, "File type");
        _enter.SelectionChanged += (_, _) =>
        {
            if (!_updating && _enter.SelectedIndex >= 0)
                Change(() => _model.SetEnter(EnterActions[_enter.SelectedIndex].Action), _afterError);
        };
        _fileType.SelectionChanged += (_, _) =>
        {
            if (!_updating && _fileType.SelectedIndex >= 0)
                Change(() => _model.SetFileType(FileTypes[_fileType.SelectedIndex].Type), _afterError);
        };
        Flip(_history, on => _model.SetHistory(on), _recentError);
        Flip(_ctrlDrag, on => _model.SetCtrlDragCopies(on), _selectingError);
        Flip(_magnifier, on => _model.SetMagnifier(on), _selectingError);

        _folder.SetResourceReference(StyleProperty, "BodyTextStyle");
        _folder.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _folderMissing.SetResourceReference(StyleProperty, "CaptionTextStyle");
        _folderMissing.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_folderMissing, AutomationLiveSetting.Polite);
        _change.Click += (_, _) => ChooseFolder();
        _reset.Click += (_, _) => Change(() => _model.SetSaveFolder(null), _afterError);
        AutomationProperties.SetName(_change, "Change save folder");
        AutomationProperties.SetName(_reset, "Reset save folder");
        _reset.Margin = new Thickness(8, 0, 0, 0);
        _change.Margin = new Thickness(8, 0, 0, 0);
        var where = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _folder, _folderMissing } };
        var folderRow = SettingsLayout.Row("Save folder", new StackPanel { Orientation = Orientation.Horizontal, Children = { where, _reset, _change } });

        _clear.Click += (_, _) => Confirm(_clear, "Clear recent captures?", "Clear", () => _model.ClearRecent());
        AutomationProperties.SetName(_clear, "Clear recent captures");
        _count.Content = _clear;
        _count.Padding = SettingsLayout.DependentPadding;
        _grid.SizeChanged += (_, e) => Columns(e.NewSize.Width);
        AutomationProperties.SetName(_grid, "Recent captures");
        // The tiles' 2-wide rings sit outside the pictures, so the pictures line up with the rows' 16 inset.
        _grid.Margin = new Thickness(0, 0, -4, -4);
        _gridBox = new Border { Padding = new Thickness(14, 2, 14, 14), Child = _grid };
        _recent = SettingsLayout.Card(SettingsLayout.Row("History", _history), _count, _gridBox);

        var shortcuts = SettingsLayout.Card();
        foreach (string name in ScreenCaptureShortcuts.All)
        {
            var setting = new ShortcutSetting(name, () => _model.EditShortcut(name));
            _shortcuts[name] = setting;
            shortcuts.Children.Add(SettingsLayout.Row(name, setting));
        }
        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Section("After a capture", first: true),
                SettingsLayout.Card(SettingsLayout.Row("Enter key", _enter), folderRow, SettingsLayout.Row("File type", _fileType)), _afterError,
                SettingsLayout.Section("Recent captures"), _recent, _recentError,
                SettingsLayout.Section("Selecting"),
                SettingsLayout.Card(SettingsLayout.Row("Ctrl+drag to copy", _ctrlDrag), SettingsLayout.Row("Magnifier", _magnifier)), _selectingError,
                SettingsLayout.Section("Shortcuts"), shortcuts,
            },
        };
        // Listen from the start, not only once loaded: the page is built before Settings shows it.
        Listen(true);
        Loaded += (_, _) => { Listen(true); Refresh(); };
        Unloaded += (_, _) => Listen(false);
        Refresh();
    }

    /// <summary>Opens the folder picker at <paramref name="current"/>; null when Travis cancels. Tests stand in for it.</summary>
    internal Func<string, string?> PickFolder { get; set; } = current =>
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Save folder", Multiselect = false };
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    };

    internal ComboBox Enter => _enter;

    internal ComboBox FileType => _fileType;

    internal ToggleButton History => _history;

    internal ToggleButton CtrlDrag => _ctrlDrag;

    internal ToggleButton Magnifier => _magnifier;

    internal TextBlock Folder => _folder;

    internal TextBlock FolderMissing => _folderMissing;

    internal Button ChangeFolder => _change;

    internal Button ResetFolder => _reset;

    internal SettingRow Count => _count;

    internal Button Clear => _clear;

    internal UniformGrid Grid => _grid;

    internal IReadOnlyList<Button> Tiles => [.. _grid.Children.OfType<Button>()];

    internal TextBlock RecentError => _recentError;

    internal TextBlock AfterError => _afterError;

    internal ShortcutSetting ShortcutOf(string name) => _shortcuts[name];

    /// <summary>The menu a capture's tile opens: Annotate, Remove from recent and, for a saved file that's still there, Delete file.</summary>
    internal ContextMenu MenuOf(Guid id) => _tiles[id].Menu();

    /// <summary>"Pictures\Screenshots" for a folder in Pictures, the rest of the path under the profile, or the whole path.</summary>
    internal static string Display(string folder)
    {
        string full = folder.TrimEnd('\\');
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures).TrimEnd('\\');
        if (pictures.Length > 0 && (full.Equals(pictures, StringComparison.OrdinalIgnoreCase) || full.StartsWith(pictures + "\\", StringComparison.OrdinalIgnoreCase)))
            return "Pictures" + full[pictures.Length..];
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        if (profile.Length > 0 && full.StartsWith(profile + "\\", StringComparison.OrdinalIgnoreCase))
            return full[(profile.Length + 1)..];
        return full;
    }

    /// <summary>"2:14 PM" today, "Nov 5, 2:14 PM" before.</summary>
    internal static string When(DateTimeOffset time, DateTimeOffset now)
    {
        var local = time.ToLocalTime();
        string clock = local.ToString("t", CultureInfo.CurrentCulture);
        return local.Date == now.ToLocalTime().Date ? clock : $"{local.ToString("MMM d", CultureInfo.CurrentCulture)}, {clock}";
    }

    private void Listen(bool on)
    {
        if (on == _listening) return;
        _listening = on;
        if (on) _model.Changed += OnChanged;
        else _model.Changed -= OnChanged;
    }

    private void OnChanged()
    {
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.BeginInvoke(() => { if (_listening) Refresh(); });
    }

    private void Refresh()
    {
        var settings = _model.Settings;
        _updating = true;
        try
        {
            _enter.SelectedIndex = Math.Max(0, EnterActions.ToList().FindIndex(a => a.Action == settings.Enter));
            _fileType.SelectedIndex = Math.Max(0, FileTypes.ToList().FindIndex(t => t.Type == settings.FileType));
            Sync(_history, settings.History);
            Sync(_ctrlDrag, settings.CtrlDragCopies);
            Sync(_magnifier, settings.Magnifier);
        }
        finally { _updating = false; }

        string folder = _model.SaveFolder;
        _folder.Text = Display(folder);
        _folder.ToolTip = folder;
        AutomationProperties.SetName(_folder, $"Save folder {folder}");
        bool missing = _model.SaveFolderMissing;
        _folderMissing.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
        _folder.SetResourceReference(TextBlock.ForegroundProperty, missing ? "TextTertiaryBrush" : "TextSecondaryBrush");
        _reset.Visibility = settings.SaveFolder is null ? Visibility.Collapsed : Visibility.Visible;

        foreach (string name in ScreenCaptureShortcuts.All)
        {
            var (keys, error) = _model.Shortcut(name);
            if (error == UsedByWindows) _shortcuts[name].ShowText(UsedByWindows);
            else _shortcuts[name].Show(keys, error);
        }
        RefreshRecent(settings.History, _model.Recent);
    }

    private void RefreshRecent(bool history, IReadOnlyList<RecentCapture> recent)
    {
        // With History off, captures still kept can be cleared; with none, only the switch shows.
        bool list = history || recent.Count > 0;
        _count.Visibility = list && recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _count.Header = recent.Count == 1 ? "1 capture" : $"{recent.Count.ToString(CultureInfo.CurrentCulture)} captures";
        _gridBox.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool none = list && recent.Count == 0;
        if (none && !_recent.Children.Contains(_none)) _recent.Children.Add(_none);
        if (!none) _recent.Children.Remove(_none);

        var now = DateTimeOffset.Now;
        var ids = recent.Select(c => c.Id).ToHashSet();
        foreach (var gone in _tiles.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _grid.Children.Remove(_tiles[gone].Button);
            _tiles.Remove(gone);
        }
        for (int i = 0; i < recent.Count; i++)
        {
            var capture = recent[i];
            if (!_tiles.TryGetValue(capture.Id, out var tile))
                _tiles[capture.Id] = tile = new Tile(this, capture.Id, _model.Thumbnail(capture.Id));
            tile.Show(capture, _model.FileMissing(capture), now);
            if (_grid.Children.IndexOf(tile.Button) == i) continue;
            _grid.Children.Remove(tile.Button);
            _grid.Children.Insert(i, tile.Button);
        }
    }

    /// <summary>Six across at the window's full width, four on a narrow one, three below that.</summary>
    private void Columns(double width)
    {
        int columns = width >= 560 ? 6 : width >= 360 ? 4 : 3;
        if (_grid.Columns != columns) _grid.Columns = columns;
    }

    private void ChooseFolder()
    {
        if (PickFolder(_model.SaveFolder) is not { Length: > 0 } folder) return; // Cancel keeps the old folder.
        Change(() => _model.SetSaveFolder(folder), _afterError);
    }

    private void Delete(Guid id, RecentCapture capture, FrameworkElement anchor)
    {
        string file = Path.GetFileName(capture.SavedFile) ?? "";
        Confirm(anchor, $"Delete {file}?", "Delete", () =>
        {
            bool deleted = _model.DeleteFile(id);
            Say(_recentError, deleted ? null : "Delete failed");
        });
    }

    private void Flip(ToggleButton toggle, Func<bool, bool> write, TextBlock error)
    {
        // Checked and Unchecked, not Click: UI Automation's Toggle raises no Click.
        toggle.Checked += (_, _) => { if (!_updating) Change(() => write(true), error); };
        toggle.Unchecked += (_, _) => { if (!_updating) Change(() => write(false), error); };
    }

    private void Change(Func<bool> write, TextBlock error)
    {
        bool saved = write();
        Refresh();
        Say(error, saved ? null : _model.SettingsError ?? "Save failed");
    }

    private static void Say(TextBlock error, string? text)
    {
        error.Text = text ?? "";
        error.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        if (text is not null) UIElementAutomationPeer.FromElement(error)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static void Sync(ToggleButton toggle, bool on)
    {
        toggle.IsChecked = on;
        toggle.Content = on ? "On" : "Off";
    }

    private static ToggleButton Switch(string name)
    {
        var toggle = new ToggleButton();
        toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(toggle, name);
        return toggle;
    }

    private static Button Button(string text, string style = "SecondaryButtonStyle")
    {
        var button = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, style);
        return button;
    }

    private static TextBlock Error()
    {
        var error = new TextBlock { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        error.SetResourceReference(StyleProperty, "BodyTextStyle");
        error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        return error;
    }

    /// <summary>A short question under <paramref name="anchor"/>, with Cancel and the verb; Esc or Cancel goes back to the anchor.</summary>
    private static void Confirm(FrameworkElement anchor, string question, string verb, Action confirmed)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None };
        var cancel = Button("Cancel");
        var act = Button(verb, "CriticalButtonStyle");
        act.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => { popup.IsOpen = false; anchor.Focus(); };
        act.Click += (_, _) => { popup.IsOpen = false; confirmed(); };
        var asked = new TextBlock { Text = question, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
        asked.SetResourceReference(StyleProperty, "BodyTextStyle");
        var body = new StackPanel
        {
            Margin = new Thickness(16),
            Children = { asked, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { cancel, act } } },
        };
        KeyboardNavigation.SetTabNavigation(body, KeyboardNavigationMode.Cycle);
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            popup.IsOpen = false;
            anchor.Focus();
            e.Handled = true;
        };
        var panel = new Border { MinWidth = 260, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = body };
        panel.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        panel.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        AutomationProperties.SetName(panel, question);
        popup.Child = panel;
        popup.Opened += (_, _) => cancel.Focus();
        popup.IsOpen = true;
    }

    /// <summary>One recent capture: its thumbnail, "File missing" on a saved file that's gone, and its menu on click.</summary>
    private sealed class Tile
    {
        private readonly ScreenCaptureSettingsPage _page;
        private readonly Guid _id;
        private readonly Border _missing;
        private RecentCapture? _capture;
        private bool _fileMissing;

        public Tile(ScreenCaptureSettingsPage page, Guid id, ImageSource? thumbnail)
        {
            _page = page;
            _id = id;
            var picture = new Border { Height = 70, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), ClipToBounds = true };
            picture.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            picture.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            var image = new Image { Source = thumbnail, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            picture.Child = image;
            picture.SizeChanged += (_, e) => image.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width - 2, e.NewSize.Height - 2), 3, 3);
            var label = new TextBlock { Text = "File missing" };
            label.SetResourceReference(StyleProperty, "CaptionTextStyle");
            label.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
            _missing = new Border { Child = label, Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(4), CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
            _missing.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
            Button = new Button { Content = new Grid { Children = { picture, _missing } }, Template = TileTemplate.Value, FocusVisualStyle = null, Margin = new Thickness(0, 0, 4, 4), Cursor = System.Windows.Input.Cursors.Hand };
            Button.Click += (_, _) =>
            {
                var menu = Menu();
                menu.PlacementTarget = Button;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
            };
            Button.ContextMenuOpening += (_, e) =>
            {
                e.Handled = true;
                var menu = Menu();
                menu.PlacementTarget = Button;
                menu.IsOpen = true;
            };
        }

        /// <summary>A 2-wide ring around the picture: the strong border on hover, the focus ring with the keyboard.</summary>
        private static readonly Lazy<ControlTemplate> TileTemplate = new(() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate TargetType="Button" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Border x:Name="Ring" BorderThickness="2" CornerRadius="6" BorderBrush="Transparent" Background="Transparent">
                <ContentPresenter />
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Ring" Property="BorderBrush" Value="{DynamicResource BorderStrongBrush}" /></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Ring" Property="BorderBrush" Value="{DynamicResource FocusRingBrush}" /></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """));

        public Button Button { get; }

        public void Show(RecentCapture capture, bool fileMissing, DateTimeOffset now)
        {
            _capture = capture;
            _fileMissing = fileMissing;
            string size = $"{capture.Width.ToString(CultureInfo.CurrentCulture)} × {capture.Height.ToString(CultureInfo.CurrentCulture)}";
            string when = When(capture.Time, now);
            _missing.Visibility = fileMissing ? Visibility.Visible : Visibility.Collapsed;
            Button.ToolTip = fileMissing ? $"{size} · {when} · File missing" : $"{size} · {when}";
            AutomationProperties.SetName(Button, fileMissing ? $"Capture, {size}, {when}, file missing" : $"Capture, {size}, {when}");
        }

        public ContextMenu Menu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(Item("Annotate", () => _page._model.Annotate(_id)));
            menu.Items.Add(Item("Remove from recent", () => _page._model.Remove(_id)));
            if (_capture is { SavedFile: { Length: > 0 } } capture && !_fileMissing)
                menu.Items.Add(Item("Delete file", () => _page.Delete(_id, capture, Button)));
            return menu;
        }

        private static MenuItem Item(string header, Action clicked)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => clicked();
            return item;
        }
    }
}
