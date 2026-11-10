// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Modules;
using Chippi.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.MediaTools;

public sealed class MediaToolsView : ContentControl, IDisposable
{
    private readonly MediaToolsModule _module;
    private readonly ModuleContext _context;
    private readonly Grid _header = new();
    private readonly Grid _body = new();
    private readonly StackPanel _options = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _extra = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _files = new();
    private readonly TextBlock _destination = Label("");
    private readonly TextBlock _summary = Label("");
    private readonly TextBlock _pageLabel = Label("");
    private readonly TextBlock _message = Label("");
    private readonly List<(MediaJob Job, TextBlock State, Button Retry, Button Compare, Button Open, Button Remove)> _rows = [];
    private readonly Button _add, _run, _cancel, _previous, _next;
    private readonly ContentControl _content = new();
    private int _page;
    private string? _structure;
    private bool _compare;
    private int _compareRequest;
    private bool _disposed;
    public FrameworkElement Header => _header;
    public UIElement InitialFocus => _add;

    public MediaToolsView(MediaToolsModule module, ModuleContext context)
    {
        _module = module; _context = context;
        Content = _content; _content.Content = _body;
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, new GridLength(28) }) _body.RowDefinitions.Add(new() { Height = height });
        _options.MinHeight = 30; _extra.Margin = new(0, 0, 0, 6);
        Add(_body, _options, 0); Add(_body, _extra, 1);
        var filesBorder = new Border { Child = _files, CornerRadius = new(6), BorderThickness = new(1), Padding = new(6, 0, 4, 0) };
        filesBorder.SetResourceReference(Border.BackgroundProperty, "IslandGalleryFaceBrush"); filesBorder.SetResourceReference(Border.BorderBrushProperty, "IslandCardBorderBrush");
        Add(_body, filesBorder, 2);
        _destination.Margin = new(0, 6, 0, 0); Add(_body, _destination, 3);
        var pager = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _previous = Button("Previous", () => { _page--; _structure = null; Refresh(); }); _next = Button("Next", () => { _page++; _structure = null; Refresh(); });
        pager.Children.Add(_previous); pager.Children.Add(_pageLabel); pager.Children.Add(_next); Add(_body, pager, 4);
        var footer = new DockPanel { LastChildFill = true };
        _run = Button("Run", () => _ = RunAsync(), true); DockPanel.SetDock(_run, Dock.Right); footer.Children.Add(_run);
        _cancel = Button("Cancel", () => module.Batch?.Cancel()); DockPanel.SetDock(_cancel, Dock.Right); footer.Children.Add(_cancel);
        _add = Button("Add files", () => _ = module.AddFilesAsync()); DockPanel.SetDock(_add, Dock.Left); footer.Children.Add(_add);
        _summary.Margin = new(8, 0, 4, 0); footer.Children.Add(_summary); Add(_body, footer, 5);
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = CanEdit && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) => { if (CanEdit && e.Data.GetData(DataFormats.FileDrop) is string[] files) await module.AddAsync(files); e.Handled = true; };
        _module.Changed += Refresh; Refresh();
    }
    private bool CanEdit => _module.Batch is { IsRunning: false, IsInspecting: false };
    private async Task RunAsync(MediaJob? retry = null)
    {
        if (_module.Batch == null) return;
        try { await _module.Batch.RunAsync(_module.Settings.Options, retry); }
        catch (Exception ex) { Report(ex.Message); }
    }
    internal void Report(string message) { _message.Text = message; _summary.Text = message; }
    public bool Back()
    {
        if (!_compare) return false;
        _compare = false; _compareRequest++; _content.Content = _body; _structure = null; Refresh(); return true;
    }
    private void Refresh()
    {
        if (_disposed || _module.Batch is not { } batch) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        if (_compare) return;
        _page = Math.Clamp(_page, 0, Math.Max(0, (batch.Jobs.Count - 1) / 5));
        string structure = $"{_page}|{batch.Jobs.Count}|{batch.IsRunning}|{batch.IsInspecting}|{_module.Settings.Options}";
        if (_structure != structure)
        {
            _structure = structure; MakeHeader(); MakeOptions(); MakeRows();
        }
        foreach (var row in _rows)
        {
            var job = row.Job;
            string? refusal = job.Source == null ? job.Error : MediaToolsCodec.Refusal(job.Source, _module.Settings.Options);
            row.State.Text = refusal ?? job.State switch
            {
                MediaJobState.Running => $"{job.Percent}%",
                MediaJobState.Done => Bytes(job.Output!.Bytes) + (job.Source!.Bytes > 0 ? $" · {(job.Output.Bytes / (double)job.Source.Bytes - 1) * 100:+0;-0;0}%" : ""),
                MediaJobState.Failed => job.Error ?? "Failed",
                MediaJobState.Cancelled => "Cancelled",
                MediaJobState.Refused => job.Error ?? "Unsupported format",
                _ => Note(job),
            };
            row.State.ToolTip = row.State.Text;
            row.Retry.Visibility = job.State == MediaJobState.Failed ? Visibility.Visible : Visibility.Collapsed;
            row.Retry.IsEnabled = CanEdit && refusal == null;
            row.Compare.Visibility = job.Output != null && job.Source?.IsVideo == false && Path.GetExtension(job.Output.Path) is ".png" or ".jpg" ? Visibility.Visible : Visibility.Collapsed;
            row.Open.Visibility = job.Output != null ? Visibility.Visible : Visibility.Collapsed;
            row.Remove.Visibility = Visibility.Visible;
            row.Remove.IsEnabled = CanEdit;
        }
        _previous.Visibility = _next.Visibility = _pageLabel.Visibility = batch.Jobs.Count > 5 ? Visibility.Visible : Visibility.Collapsed;
        _previous.IsEnabled = _page > 0; _next.IsEnabled = (_page + 1) * 5 < batch.Jobs.Count;
        _pageLabel.Text = $" {_page + 1} / {Math.Max(1, (batch.Jobs.Count + 4) / 5)} ";
        _add.IsEnabled = CanEdit;
        _run.Visibility = batch.IsRunning ? Visibility.Collapsed : Visibility.Visible;
        _cancel.Visibility = batch.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        _run.IsEnabled = CanEdit && batch.Jobs.Any(j => j.Source != null && j.State != MediaJobState.Done && MediaToolsCodec.Refusal(j.Source, _module.Settings.Options) == null);
        int done = batch.Jobs.Count(j => j.State == MediaJobState.Done), failed = batch.Jobs.Count(j => j.State == MediaJobState.Failed);
        _summary.Text = batch.Error ?? (batch.IsInspecting ? "Reading files…" : batch.IsRunning ? $"{done + failed} / {batch.Jobs.Count}" : failed > 0 ? $"{done} done · {failed} failed" : done > 0 ? $"{done} done" : "");
        _destination.Text = string.IsNullOrWhiteSpace(_module.Settings.Options.OutputFolder) ? "Saves beside the originals" : _module.Settings.Options.OutputFolder;
        _destination.ToolTip = _destination.Text;
    }
    private string Note(MediaJob job)
    {
        if (job.Source is not { } source) return "Reading…";
        var options = _module.Settings.Options;
        var size = options.SizeFor(source);
        if (size.Width > source.Width || size.Height > source.Height) return "Enlarge";
        if (source.Animated && options.OutputFor(source) is MediaOutputFormat.Png or MediaOutputFormat.Jpeg) return "First frame";
        return "Waiting";
    }
    private void MakeHeader()
    {
        _header.Children.Clear(); _header.ColumnDefinitions.Clear(); _header.ColumnDefinitions.Add(new()); _header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = Label("Convert"); title.FontSize = 13; title.FontWeight = FontWeights.SemiBold; _header.Children.Add(title);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var tool in Enum.GetValues<MediaTool>())
        {
            var button = Button(tool.ToString(), () => _module.SaveOptions(_module.Settings.Options with { Tool = tool }));
            button.Height = 24; button.MinWidth = 0; button.FontSize = 11; button.Padding = new(8, 0, 8, 0); button.Margin = new(0);
            button.IsEnabled = CanEdit; button.FontWeight = _module.Settings.Options.Tool == tool ? FontWeights.SemiBold : FontWeights.Normal;
            if (_module.Settings.Options.Tool == tool) button.SetResourceReference(BackgroundProperty, "IslandButtonBrush");
            tabs.Children.Add(button);
        }
        Grid.SetColumn(tabs, 1); _header.Children.Add(tabs);
    }
    private void MakeOptions()
    {
        _options.Children.Clear(); _extra.Children.Clear();
        var options = _module.Settings.Options;
        var count = Label($"{_module.Batch!.Jobs.Count} files · {Bytes(_module.Batch.Jobs.Sum(j => j.Source?.Bytes ?? 0))}"); count.Width = 120; count.FontSize = 11; _options.Children.Add(count);
        if (options.Tool == MediaTool.Convert)
        {
            _options.Children.Add(Label("Format"));
            _options.Children.Add(Choice(new[] { "PNG", "JPEG", "MP4", "GIF" }, (int)options.Format, i => _module.SaveOptions(options with { Format = (MediaOutputFormat)i })));
            if (options.Format is MediaOutputFormat.Jpeg or MediaOutputFormat.Mp4)
            {
                _options.Children.Add(Label("Quality"));
                _options.Children.Add(Choice(Enum.GetNames<MediaQuality>(), (int)options.Quality, i => _module.SaveOptions(options with { Quality = (MediaQuality)i })));
            }
            else _options.Children.Add(Label(options.Format == MediaOutputFormat.Png ? "Lossless" : "15 fps"));
        }
        else if (options.Tool == MediaTool.Resize)
        {
            string[] sizes = ["50%", "75%", "100%", "150%", "200%", "1920 × 1080", "1280 × 720", "640 × 480", "Custom"];
            int selected = options.Percent switch { 50 => 0, 75 => 1, 100 => 2, 150 => 3, 200 => 4, > 0 => 8, _ => options.Width == 1920 && options.Height == 1080 ? 5 : options.Width == 1280 && options.Height == 720 ? 6 : options.Width == 640 && options.Height == 480 ? 7 : 8 };
            _options.Children.Add(Label("Size")); _options.Children.Add(Choice(sizes, selected, i =>
            {
                int[] percent = [50, 75, 100, 150, 200];
                var dims = i switch { 5 => (1920, 1080), 6 => (1280, 720), 7 => (640, 480), _ => (options.Width, options.Height) };
                _module.SaveOptions(options with { Percent = i < 5 ? percent[i] : 0, Width = dims.Item1, Height = dims.Item2 });
            }));
            _options.Children.Add(Label("Image fit")); _options.Children.Add(Choice(Enum.GetNames<MediaFit>(), (int)options.Fit, i => _module.SaveOptions(options with { Fit = (MediaFit)i })));
            if (options.Percent == 0)
            {
                var width = Input(options.Width.ToString(CultureInfo.InvariantCulture), "Width in pixels", 66);
                var height = Input(options.Height.ToString(CultureInfo.InvariantCulture), "Height in pixels", 66);
                void SizeChanged() { if (int.TryParse(width.Text, out int w) && int.TryParse(height.Text, out int h)) _module.SaveOptions(options with { Width = w, Height = h }); else Report("Enter a valid size"); }
                width.LostKeyboardFocus += (_, _) => SizeChanged(); height.LostKeyboardFocus += (_, _) => SizeChanged();
                _extra.Children.Add(width); _extra.Children.Add(Label("×")); _extra.Children.Add(height);
            }
            else
            {
                _extra.Children.Add(Label("Scale")); var percent = Input(options.Percent.ToString(CultureInfo.InvariantCulture), "Scale percentage", 66);
                percent.LostKeyboardFocus += (_, _) => { if (int.TryParse(percent.Text, out int value) && value is >= 1 and <= 400) _module.SaveOptions(options with { Percent = value }); else Report("Enter a percentage from 1 to 400"); };
                _extra.Children.Add(percent); _extra.Children.Add(Label("%"));
            }
        }
        else
        {
            _options.Children.Add(Label("Level")); _options.Children.Add(Choice(Enum.GetNames<MediaQuality>(), (int)options.Quality, i => _module.SaveOptions(options with { Quality = (MediaQuality)i })));
        }
        if (options.Tool == MediaTool.Compress || options.Tool == MediaTool.Convert && options.Format == MediaOutputFormat.Jpeg)
        {
            _extra.Children.Add(Label("Background"));
            var color = Input($"#{options.Background:X6}", "JPEG background color", 88);
            color.LostKeyboardFocus += (_, _) => { if (TryColor(color.Text, out uint rgb)) _module.SaveOptions(options with { Background = rgb }); else Report("Enter a six-digit color"); };
            _extra.Children.Add(color);
        }
        _extra.Visibility = _extra.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void MakeRows()
    {
        _files.Children.Clear(); _files.RowDefinitions.Clear(); _rows.Clear();
        var batch = _module.Batch!;
        if (batch.Jobs.Count == 0)
        {
            var empty = Label("Drop files here"); empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center; _files.Children.Add(empty); return;
        }
        foreach (var job in batch.Jobs.Skip(_page * 5).Take(5))
        {
            int index = _files.RowDefinitions.Count; _files.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star), MaxHeight = 34 });
            var line = new Grid { Margin = new(0, 0, 0, 1) };
            foreach (var column in new[] { new GridLength(1, GridUnitType.Star), new GridLength(88), new GridLength(104), GridLength.Auto }) line.ColumnDefinitions.Add(new() { Width = column });
            var name = Label(job.Name); name.ToolTip = job.Path; line.Children.Add(name);
            var info = Label(job.Source is { } source ? Bytes(source.Bytes) + " · " + source.Extension : ""); info.FontSize = 11; AddColumn(line, info, 1);
            var state = Label(""); state.FontSize = 11; AddColumn(line, state, 2);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var retry = Button("Retry", () => _ = RunAsync(job));
            var compare = Button("Compare", () => _ = CompareAsync(job));
            var open = Button("Open", () => OpenOutput(job));
            var remove = Button("Remove", () => { batch.Remove(job); _structure = null; Refresh(); });
            var removeIcon = new LucideIcon { Data = LucideIcons.Close, Size = 12 }; removeIcon.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
            remove.Content = removeIcon; remove.MinWidth = 22;
            AutomationProperties.SetName(remove, "Remove " + job.Name); remove.ToolTip = "Remove " + job.Name;
            foreach (var button in new[] { retry, compare, open, remove }) { button.FontSize = 11; button.Padding = new(4, 0, 4, 0); buttons.Children.Add(button); }
            AddColumn(line, buttons, 3); Add(_files, line, index); _rows.Add((job, state, retry, compare, open, remove));
        }
    }
    internal async Task CompareAsync(MediaJob job)
    {
        if (job.Output is not { } output || job.Source is not { } source) return;
        int request = ++_compareRequest;
        _compare = true; _header.Children.Clear(); _header.Children.Add(Label("Compare"));
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        _content.Content = grid;
        try
        {
            foreach (var (path, bytes, column, title) in new[] { (job.Path, source.Bytes, 0, "Original"), (output.Path, output.Bytes, 1, "Export") })
            {
                byte[] png = await Task.Run(() => MediaToolsCodec.PreviewAsync(path));
                if (!_compare || _disposed || request != _compareRequest) return;
                var image = new BitmapImage(); using var stream = new MemoryStream(png); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
                var stack = new StackPanel { Margin = new(4) }; stack.Children.Add(Label(title + " · " + Bytes(bytes))); stack.Children.Add(new Image { Source = image, Height = 240, Stretch = Stretch.Uniform });
                AddColumn(grid, stack, column);
            }
        }
        catch (Exception ex) { if (request == _compareRequest && !_disposed) { Back(); Report("Compare failed · " + ex.Message); } }
    }
    private void OpenOutput(MediaJob job)
    {
        if (job.Output == null) return;
        using var hold = _context.HoldIslandOpen();
        try { _context.Actions?.Open(job.Output.Path); } catch (Exception ex) { Report("Open failed · " + ex.Message); }
    }
    private ComboBox Choice(string[] names, int selected, Action<int> pick)
    {
        var combo = new ComboBox { ItemsSource = names, SelectedIndex = selected, MinWidth = 72, Height = 24, FontSize = 11, Margin = new(4, 0, 8, 0), IsEnabled = CanEdit };
        combo.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
        AutomationProperties.SetName(combo, _options.Children.OfType<TextBlock>().LastOrDefault()?.Text ?? "Option");
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) pick(combo.SelectedIndex); }; return combo;
    }
    private TextBox Input(string text, string name, double width)
    {
        var input = new TextBox { Text = text, Width = width, Height = 24, FontSize = 12, IsEnabled = CanEdit, Margin = new(4, 0, 8, 0) };
        input.SetResourceReference(StyleProperty, "TextFieldStyle");
         AutomationProperties.SetName(input, name); return input;
    }
    internal static Button Button(string name, Action action, bool primary = false)
    {
        var button = new Button { Content = name, Height = 28, Padding = new(10, 0, 10, 0), Margin = new(0, 0, 4, 0) };
        button.SetResourceReference(StyleProperty, primary ? "IslandAccentButtonStyle" : "IslandButtonStyle"); button.Click += (_, _) => action(); return button;
    }
    internal static TextBlock Label(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        label.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextSecondaryBrush"); return label;
    }
    internal static string Bytes(long count) => count >= 1_000_000_000 ? $"{count / 1_000_000_000d:0.#} GB" : count >= 1_000_000 ? $"{count / 1_000_000d:0.#} MB" : count >= 1_000 ? $"{count / 1_000d:0.#} KB" : $"{count} B";
    internal static bool TryColor(string value, out uint color)
    {
        string hex = value.Trim().TrimStart('#');
        color = 0;
        return hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color);
    }
    private static void Add(Grid grid, UIElement element, int row) { Grid.SetRow(element, row); grid.Children.Add(element); }
    private static void AddColumn(Grid grid, UIElement element, int column) { Grid.SetColumn(element, column); grid.Children.Add(element); }
    public void Dispose() { _disposed = true; _module.Changed -= Refresh; }
}
