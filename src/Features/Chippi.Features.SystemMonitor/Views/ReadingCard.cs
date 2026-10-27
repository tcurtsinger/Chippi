// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// One reading's card (design notes #7, #10): its name in section-label style, an optional device
/// name or picker, the headline value in the Reading role (20 / 400, tabular) at the right, then the
/// graph and one or two "state · detail" lines.
/// </summary>
internal sealed class ReadingCard
{
    private readonly TextBlock _title;
    private readonly Border _device = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _headline = new() { FontSize = 20, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly StackPanel _lines = new();

    public ReadingCard(string name, bool graph)
    {
        _title = SystemMonitorView.Header(name);
        Typography.SetNumeralAlignment(_headline, FontNumeralAlignment.Tabular);
        // Name, then the device (trimmed when long), then the headline at the right.
        var header = new Grid
        {
            Height = 28,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            Children = { _title, _device, _headline },
        };
        Grid.SetColumn(_device, 1);
        Grid.SetColumn(_headline, 2);
        Body = new StackPanel { Margin = new Thickness(0, graph ? 6 : 2, 0, 0) };
        if (graph)
        {
            Graph = new Graph();
            Body.Children.Add(Graph);
            _lines.Margin = new Thickness(0, 6, 0, 0);
        }
        Body.Children.Add(_lines);
        Body.Children.Add(Detail);
        Card = SystemMonitorView.Card();
        Card.Margin = new Thickness(0, 0, 0, 8);
        Card.Tag = this;
        Card.Child = new StackPanel { Children = { header, Body } };
        Card.Visibility = Visibility.Collapsed;
    }

    public Border Card { get; }

    /// <summary>Everything under the header.</summary>
    public StackPanel Body { get; }

    /// <summary>Under the lines: CPU's logical processor strip.</summary>
    public StackPanel Detail { get; } = new();

    public Graph? Graph { get; }

    /// <summary>Shows or hides the card; returns <paramref name="visible"/>, so a caller can stop there.</summary>
    public bool Visible(bool visible)
    {
        Card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        return visible;
    }

    public void Rename(string name) => _title.Text = name;

    /// <summary>The device's name, plain text or a picker, after the card's name.</summary>
    public void SetName(FrameworkElement device) => _device.Child = device;

    /// <summary>The headline value, in Caution only while its alert is on and past its threshold (design notes #18).</summary>
    public void Headline(string value, bool caution)
    {
        _headline.Text = value;
        _headline.SetResourceReference(TextBlock.ForegroundProperty, caution ? "WarningTextBrush" : "IslandTextBrush");
        AutomationProperties.SetName(Card, $"{_title.Text} {value}");
    }

    /// <summary>A failed read: no headline, a short status, and the graph's gap.</summary>
    public void Unavailable()
    {
        ClearHeadline();
        SetLine(SystemMonitorView.Status("Unavailable"));
    }

    /// <summary>No value right now: the headline is empty rather than the last one.</summary>
    public void ClearHeadline()
    {
        _headline.Text = "";
        AutomationProperties.SetName(Card, _title.Text);
    }

    /// <summary>One line, with <paramref name="right"/> (CPU's processors button) at its end.</summary>
    public void SetLine(TextBlock line, FrameworkElement? right = null)
    {
        _lines.Children.Clear();
        if (right is null)
        {
            _lines.Children.Add(line);
            return;
        }
        if (right.Parent is Panel old)
            old.Children.Remove(right);
        DockPanel.SetDock(right, Dock.Right);
        line.VerticalAlignment = VerticalAlignment.Center;
        _lines.Children.Add(new DockPanel { Children = { right, line } });
    }

    /// <summary>Two lines; an empty one is left out.</summary>
    public void SetLines(TextBlock first, TextBlock second)
    {
        _lines.Children.Clear();
        foreach (var line in new[] { first, second }.Where(l => l.Inlines.Count > 0))
        {
            if (_lines.Children.Count > 0)
                line.Margin = new Thickness(0, 2, 0, 0);
            _lines.Children.Add(line);
        }
    }
}
