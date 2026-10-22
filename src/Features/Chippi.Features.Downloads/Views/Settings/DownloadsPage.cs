// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Storage;
using Chippi.UI.Controls;

namespace Chippi.Features.Downloads.Views.Settings;

/// <summary>
/// The organizer's page in Settings: a Settings · Rules · History segment over the section, and
/// Preview shown in place of the section with its own back button. The shell draws the title and
/// the back-to-Features button. Sections are the deep-link targets (<see cref="DownloadsModule.SettingsLink"/>):
/// <c>settings</c>, <c>rules</c>, <c>history[/runId]</c>, <c>review</c>, <c>preview/&lt;folder&gt;</c>.
/// </summary>
public sealed class DownloadsPage : UserControl
{
    private readonly DownloadsModule _module;
    private readonly SegmentedControl _segment;
    private readonly ContentControl _host = new();
    private readonly string[] _sections = ["settings", "rules", "history"];
    private bool _switching;

    public DownloadsPage(DownloadsModule module, string? section)
    {
        _module = module;
        _segment = Ui.Segment("Section", 36, "Settings", "Rules", "History");
        _segment.Width = 270;
        _segment.HorizontalAlignment = HorizontalAlignment.Right;
        _segment.Margin = new Thickness(0, -54, 0, 18); // level with the shell's page title
        _segment.SelectionChanged += (_, _) =>
        {
            if (!_switching && _segment.SelectedIndex >= 0)
                Show(_sections[_segment.SelectedIndex]);
        };
        var page = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_segment, Dock.Top);
        page.Children.Add(_segment);
        page.Children.Add(_host);
        Content = page;
        Open(section);
    }

    /// <summary>Opens a deep-link section.</summary>
    public void Open(string? section)
    {
        string head = section?.Split('/', 2)[0].ToLowerInvariant() ?? "settings";
        string? tail = section?.Split('/', 2) is [_, var rest] ? rest : null;
        switch (head)
        {
            case "preview" when tail is { Length: > 0 }:
                Preview(tail);
                break;
            case "review":
                // The first folder with something waiting, else the first folder.
                var folders = _module.Settings.Folders;
                var pending = _module.Engine?.Pending ?? [];
                string? folder = folders.FirstOrDefault(f => pending.Any(p => string.Equals(p.Folder, f.Path, StringComparison.OrdinalIgnoreCase)))?.Path
                    ?? folders.FirstOrDefault()?.Path;
                if (folder is null)
                    Show("settings");
                else
                    Preview(folder);
                break;
            case "history":
                Show("history", long.TryParse(tail, out long run) ? run : null);
                break;
            default:
                Show(_sections.Contains(head) ? head : "settings");
                break;
        }
    }

    private void Show(string section, long? runId = null)
    {
        _switching = true;
        _segment.SelectedIndex = Array.IndexOf(_sections, section);
        _switching = false;
        _segment.Visibility = Visibility.Visible;
        if (_module.Engine is null)
        {
            _host.Content = Ui.Text("Downloads organizer is off", "BodyTextStyle", "TextTertiaryBrush");
            return;
        }
        _host.Content = section switch
        {
            "rules" => new RulesSection(_module),
            "history" => new HistorySection(_module, runId),
            _ => new SettingsSection(_module, ShowImport),
        };
    }

    /// <summary>Preview for a folder (Organize now, the Explorer menu, a held row's Review).</summary>
    public void Preview(string folder)
    {
        if (_module.Engine is not { } engine)
        {
            Show("settings");
            return;
        }
        string back = CurrentSection();
        ShowPreview(new PreviewView(_module, () => engine.CreatePreviewAsync(folder), canPreviewAgain: true, () => Show(back)));
    }

    private void ShowImport(Task<PreviewPlan> plan)
    {
        string back = CurrentSection();
        ShowPreview(new PreviewView(_module, () => plan, canPreviewAgain: false, () => Show(back)));
    }

    private void ShowPreview(PreviewView view)
    {
        _segment.Visibility = Visibility.Collapsed;
        _host.Content = view;
    }

    private string CurrentSection() => _segment.SelectedIndex >= 0 ? _sections[_segment.SelectedIndex] : "settings";
}
