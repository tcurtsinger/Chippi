// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.Core.Modules;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Storage;
using Chippi.UI.Controls;

namespace Chippi.Features.Downloads.Views.Settings;

/// <summary>
/// The organizer's page in Settings: a Settings · Rules · History SelectorBar under the shell's title,
/// and Preview shown in place of the section with its own back button. The shell draws the title.
/// Sections are the deep-link targets (<see cref="DownloadsModule.SettingsLink"/>):
/// <c>settings</c>, <c>rules</c>, <c>history[/runId[/undo]]</c>, <c>review</c>, <c>preview/&lt;folder&gt;</c>, <c>explorer/&lt;folder&gt;</c>.
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
        // "Folders", not "Settings": the whole window is Settings (the section's key stays "settings").
        _segment = Ui.SelectorBar("Section", "Folders", "Rules", "History");
        _segment.SelectionChanged += (_, _) =>
        {
            if (_switching || _segment.SelectedIndex < 0)
                return;
            string section = _sections[_segment.SelectedIndex];
            if (!Leave(() => Show(section)))
                SelectTab("rules"); // the Rules tab asked first; stay on it
        };
        var page = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_segment, Dock.Top);
        page.Children.Add(_segment);
        page.Children.Add(_host);
        Content = page;
        Open(section);
    }

    /// <summary>Runs <paramref name="next"/> now, or once the Rules tab's unsaved drafts are discarded. False when it waits.</summary>
    private bool Leave(Action next)
    {
        if (_host.Content is ISettingsLeaveGuard guard && !guard.ConfirmLeave(next))
            return false;
        next();
        return true;
    }

    private void SelectTab(string section)
    {
        _switching = true;
        _segment.SelectedIndex = Array.IndexOf(_sections, section);
        _switching = false;
    }

    /// <summary>Opens a deep-link section (asking first if the Rules tab has unsaved drafts).</summary>
    public void Open(string? section)
    {
        if (!Leave(() => OpenNow(section)))
            SelectTab("rules");
    }

    private void OpenNow(string? section)
    {
        string head = section?.Split('/', 2)[0].ToLowerInvariant() ?? "settings";
        string? tail = section?.Split('/', 2) is [_, var rest] ? rest : null;
        switch (head)
        {
            case "explorer" when tail is { Length: > 0 }:
                Preview(tail, RunTrigger.Explorer);
                break;
            case "preview" when tail is { Length: > 0 }:
                // preview/<folder>*<file>: that file first (a held row's Review). No Windows path holds a '*'.
                int star = tail.LastIndexOf('*');
                if (star < 0)
                    Preview(tail);
                else
                    Preview(tail[..star], focus: tail[(star + 1)..]);
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
                // history/<run>/undo opens that run with its Undo confirmation showing; nothing is undone yet.
                // history/in/<folder> shows that folder's runs (the island's History ›).
                if (tail?.StartsWith("in/", StringComparison.Ordinal) == true)
                {
                    Show("history", folder: tail[3..]);
                    break;
                }
                string[] parts = tail?.Split('/') ?? [];
                Show("history", parts.Length > 0 && long.TryParse(parts[0], out long run) ? run : null, confirmUndo: parts is [_, "undo"]);
                break;
            default:
                Show(_sections.Contains(head) ? head : "settings");
                break;
        }
    }

    private void Show(string section, long? runId = null, bool confirmUndo = false, string? folder = null)
    {
        SelectTab(section);
        _segment.Visibility = Visibility.Visible;
        if (_module.Engine is null)
        {
            _host.Content = Ui.Text("File organizer is off", "BodyTextStyle", "TextTertiaryBrush");
            return;
        }
        _host.Content = section switch
        {
            "rules" => new RulesSection(_module),
            "history" => new HistorySection(_module, runId, confirmUndo, folder),
            _ => new SettingsSection(_module, ShowImport, folder => Preview(folder)),
        };
    }

    /// <summary>Preview for a folder (Organize now, the Explorer menu, a held row's Review with its <paramref name="focus"/> file).</summary>
    public void Preview(string folder, RunTrigger trigger = RunTrigger.Preview, string? focus = null)
    {
        if (_module.Engine is not { } engine)
        {
            Show("settings");
            return;
        }
        string back = CurrentSection();
        ShowPreview(new PreviewView(_module, () => engine.CreatePreviewAsync(folder, trigger), canPreviewAgain: true, () => Show(back), AddRule) { ShowFirst = focus });
    }

    private void ShowImport(Task<PreviewPlan> plan)
    {
        string back = CurrentSection();
        ShowPreview(new PreviewView(_module, () => plan, canPreviewAgain: false, () => Show(back), AddRule));
    }

    private void AddRule()
    {
        Show("rules");
        if (_host.Content is RulesSection rules) rules.AddRule();
    }

    private void ShowPreview(PreviewView view)
    {
        _segment.Visibility = Visibility.Collapsed;
        _host.Content = view;
    }

    private string CurrentSection() => _segment.SelectedIndex >= 0 ? _sections[_segment.SelectedIndex] : "settings";
}
