// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.Core.Actions;
using Chippi.Core.Diagnostics;
using Chippi.UI.Controls;

namespace Chippi.Features.WindowLayouts.Views;

/// <summary>
/// The island's Snap menu (docs/phase-6/design/actions-shortcuts.html, sheet-menus and sheet-snap-states): the window that
/// was in front before Chippi opened, the first six layouts as pictures whose zones are pressed to place it, then
/// Maximize, Center, Next monitor and Restore. A move that fails says so at the line's right ("Left half failed") and the
/// menu stays open to try another. An admin window, or none, dims everything with the reason.
/// </summary>
internal sealed class SnapMenu : UserControl
{
    private const double Dim = 0.45;
    private readonly WindowLayoutsModule _module;
    private readonly ActionWindow? _target;
    private readonly TextBlock _failed;
    private bool _running;
    private Button? _restore;

    public SnapMenu(WindowLayoutsModule module) : this(module, module.CaptureTarget())
    {
    }

    internal SnapMenu(WindowLayoutsModule module, ActionWindow? target)
    {
        _module = module;
        _target = target;
        bool usable = target is { Elevated: false };
        var stack = new StackPanel();

        // The target's line: its app, or why nothing can move; a failure at the right.
        _failed = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _failed.SetResourceReference(TextBlock.ForegroundProperty, "IslandCautionBrush");
        AutomationProperties.SetLiveSetting(_failed, AutomationLiveSetting.Polite);
        var who = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 0, 0) };
        who.Text = target switch
        {
            null => "No window to move",
            { Elevated: true } => $"{target.App} runs as admin, so it can't be moved",
            _ => target.App,
        };
        who.SetResourceReference(TextBlock.ForegroundProperty, usable ? "TextPrimaryBrush" : target is null ? "TextSecondaryBrush" : "IslandCautionBrush");
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 6), MinHeight = 22 };
        DockPanel.SetDock(_failed, Dock.Right);
        line.Children.Add(_failed);
        if (target is not null)
            line.Children.Add(Icon(target));
        line.Children.Add(who);
        stack.Children.Add(line);

        // The first six layouts, three to a row.
        var grid = new UniformGrid3();
        var layouts = module.Layouts?.Settings.Layouts.Take(SnapPage.InMenu).ToList() ?? [];
        foreach (var layout in layouts)
            grid.Children.Add(Picture(layout, usable));
        stack.Children.Add(grid);

        // The commands.
        var commands = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        bool restore = usable && module.CanRestore(target!);
        bool next = usable && module.Displays.Count > 1;
        var verbs = new (WindowAction Verb, string Name, Geometry Icon, bool Enabled)[]
        {
            (WindowAction.Maximize, "Maximize", LucideIcons.Maximize, usable),
            (WindowAction.Center, "Center", LucideIcons.Square, usable),
            (WindowAction.OtherScreen, "Next monitor", LucideIcons.OtherScreen, next),
            (WindowAction.Restore, "Restore", LucideIcons.Undo, restore),
        };
        for (int i = 0; i < verbs.Length; i++)
        {
            commands.ColumnDefinitions.Add(new ColumnDefinition());
            var (verb, name, icon, enabled) = verbs[i];
            var button = Command(name, icon, enabled);
            if (verb == WindowAction.Restore) _restore = button;
            button.Margin = new Thickness(i == 0 ? 0 : 4, 0, i == verbs.Length - 1 ? 0 : 4, 0);
            button.Click += (_, _) => Run(name, () => _module.RunAsync(_target!, verb));
            Grid.SetColumn(button, i);
            commands.Children.Add(button);
        }
        stack.Children.Add(commands);
        Content = stack;
    }

    /// <summary>The failure line's text, for tests.</summary>
    internal string? Failure => _failed.Visibility == Visibility.Visible ? _failed.Text : null;

    private async void Run(string what, Func<Task> run)
    {
        if (_running || _target is null) return;
        _running = true;
        try
        {
            await run();
            _failed.Visibility = Visibility.Collapsed;
            if (_restore is not null) _restore.IsEnabled = _module.CanRestore(_target);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn($"Snap: {what} failed", ex);
            _failed.Text = $"{what} failed";
            _failed.Visibility = Visibility.Visible;
        }
        finally { _running = false; }
    }

    private static FrameworkElement Icon(ActionWindow target)
    {
        string name = target.App.Length > 0 ? target.App : "?";
        var letter = new TextBlock { Text = name[..Math.Min(2, name.Length)], FontSize = 9, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        letter.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var monogram = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Child = letter };
        monogram.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        var icon = AppIcon.View(target.Executable, 18, monogram);
        icon.VerticalAlignment = VerticalAlignment.Center;
        return icon;
    }

    /// <summary>A layout's picture: each zone a button that places the window there, and the layout's name under it.</summary>
    private FrameworkElement Picture(SnapLayout layout, bool usable)
    {
        var zones = layout.Root.Flatten().Zones;
        var canvas = new ZonePicture();
        foreach (var zone in zones)
        {
            var button = new MenuZone(zone) { IsEnabled = usable };
            AutomationProperties.SetName(button, $"{layout.Name}: {zone.Name}");
            var key = new SnapZoneKey(layout.Id, zone.Id);
            button.Click += (_, _) => Run(zone.Name, () => _module.RunAsync(_target!, key));
            canvas.Children.Add(button);
        }
        var name = new TextBlock { Text = layout.Name, FontSize = 12, Margin = new Thickness(2, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var card = new Border { Padding = new Thickness(5), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(3), Opacity = usable ? 1 : Dim, Child = new StackPanel { Children = { canvas, name } } };
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return card;
    }

    private static Button Command(string name, Geometry icon, bool enabled)
    {
        var glyph = new LucideIcon { Data = icon, Size = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
        var label = new TextBlock { Text = name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var button = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { glyph, label } }, IsEnabled = enabled, Height = 36, Padding = new Thickness(6, 0, 6, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>Three equal columns; rows as many as needed.</summary>
    private sealed class UniformGrid3 : Panel
    {
        protected override Size MeasureOverride(Size available)
        {
            double width = double.IsInfinity(available.Width) ? 400 : available.Width;
            double cell = width / 3, height = 0;
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                InternalChildren[i].Measure(new Size(cell, double.PositiveInfinity));
                if (i % 3 == 0) height += InternalChildren[i].DesiredSize.Height;
            }
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double cell = final.Width / 3, y = 0, row = 0;
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                if (i % 3 == 0) { y += row; row = InternalChildren[i].DesiredSize.Height; }
                InternalChildren[i].Arrange(new Rect(i % 3 * cell, y, cell, row));
            }
            return final;
        }
    }

    /// <summary>A layout's zones, each child a <see cref="MenuZone"/> in its place.</summary>
    private sealed class ZonePicture : Panel
    {
        protected override Size MeasureOverride(Size available)
        {
            // Half as tall as wide: two rows of six pictures and the commands fit the island's 304 high body.
            double width = double.IsInfinity(available.Width) ? 120 : available.Width;
            foreach (UIElement child in InternalChildren) child.Measure(new Size(width, width / 2));
            return new Size(width, width / 2);
        }

        protected override Size ArrangeOverride(Size final)
        {
            const double gap = 3;
            foreach (MenuZone z in InternalChildren)
                z.Arrange(new Rect(z.Zone.X * final.Width + gap / 2, z.Zone.Y * final.Height + gap / 2,
                    Math.Max(0, z.Zone.Width * final.Width - gap), Math.Max(0, z.Zone.Height * final.Height - gap)));
            return final;
        }
    }

    /// <summary>A zone of a picture: pointing at it or focusing it lights it in the accent.</summary>
    internal sealed class MenuZone : Button
    {
        public MenuZone(SnapZone zone)
        {
            Zone = zone;
            Template = new ControlTemplate(typeof(MenuZone));
            FocusVisualStyle = null;
            ToolTip = zone.Name;
        }

        public SnapZone Zone { get; }

        protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e) { base.OnIsKeyboardFocusedChanged(e); InvalidateVisual(); }

        protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e) { base.OnMouseEnter(e); InvalidateVisual(); }

        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e) { base.OnMouseLeave(e); InvalidateVisual(); }

        protected override void OnRender(DrawingContext dc)
        {
            bool lit = IsEnabled && (IsMouseOver || IsKeyboardFocused);
            var accent = (Brush)FindResource("AccentBrush");
            var color = accent is SolidColorBrush s ? s.Color : Colors.MediumPurple;
            // The resting fill is the text colour, faint, so zones read as blocks on the island in both themes.
            var text = FindResource("TextPrimaryBrush") is SolidColorBrush t ? t.Color : Colors.White;
            Brush fill = lit ? new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B)) : new SolidColorBrush(Color.FromArgb(0x1C, text.R, text.G, text.B));
            var pen = lit ? new Pen(accent, 1.5) : null;
            dc.DrawRoundedRectangle(fill, pen, new Rect(0.75, 0.75, Math.Max(0, ActualWidth - 1.5), Math.Max(0, ActualHeight - 1.5)), 3, 3);
        }
    }
}
