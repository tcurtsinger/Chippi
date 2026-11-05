// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Clipboard;
using Chippi.Core.Modules;

namespace Chippi.Core.Actions;

/// <summary>
/// Runs an action (docs/phase-6/architecture.md section 3), and tells which actions' targets are gone. Each run throws
/// <see cref="ActionFailure"/> with the reason the bar shows.
/// </summary>
/// <param name="commands">Chippi's commands, as <see cref="ModuleContext.Commands"/> lists them.</param>
/// <param name="searchUrl">The web search's address for a text: the Command bar's engine.</param>
/// <param name="beforeIsland">The window in front before the island opened (<see cref="WindowTarget.BeforeIsland"/>); 0 when there's none.</param>
public sealed class ActionRunner(IActionPc pc, Func<IReadOnlyList<ChippiCommand>> commands, Func<IClipboardHub?> clipboard,
    Func<string, string> searchUrl, Func<nint>? beforeIsland = null)
{
    /// <summary>The Chippi commands an action may run: everything Chippi does but its clipboard copies, which come and go.</summary>
    public IReadOnlyList<ChippiCommand> ChippiCommands() => [.. Read(commands).Where(c => c.Kind != ChippiCommandKind.Paste)];

    private static IReadOnlyList<ChippiCommand> Read(Func<IReadOnlyList<ChippiCommand>> commands)
    {
        try { return commands(); }
        catch (Exception) { return []; }
    }

    /// <summary>
    /// Whether the action's target is gone: an app no longer installed, a file or program moved, a Chippi command whose
    /// feature isn't running. The Start menu's apps count only once they've been read.
    /// </summary>
    public bool IsMissing(ChippiAction action, IReadOnlyList<ChippiCommand>? known = null) => action.Kind switch
    {
        ActionKind.App when Path.IsPathFullyQualified(action.Value) => !pc.Exists(action.Value),
        ActionKind.App => pc.HasApp(action.Value) == false,
        ActionKind.File => !pc.Exists(action.Value),
        ActionKind.Chippi => !(known ?? ChippiCommands()).Any(c => c.Key == action.Value),
        _ => false,
    };

    /// <summary>The window a Window action fired through a door acts on, read now; 0 when there's none.</summary>
    public nint WindowOf(WindowTarget target) => target switch
    {
        WindowTarget.UnderPointer => pc.WindowUnderPointer(),
        WindowTarget.InFront => pc.WindowInFront(),
        _ => beforeIsland?.Invoke() ?? 0,
    };

    /// <summary>Runs the action; a Window action acts on the door's <paramref name="target"/>, read as it runs.</summary>
    public Task RunAsync(ChippiAction action, WindowTarget target) =>
        RunAsync(action, action.Kind == ActionKind.Window ? WindowOf(target) : 0);

    /// <param name="window">The window a Window action acts on, as its door read it: the Radial menu's, when the wheel opened.</param>
    public async Task RunAsync(ChippiAction action, nint window)
    {
        switch (action.Kind)
        {
            case ActionKind.App:
                pc.Open(Path.IsPathFullyQualified(action.Value) ? action.Value : @"shell:AppsFolder\" + action.Value);
                break;
            case ActionKind.File:
                if (!pc.Exists(action.Value))
                    throw new ActionFailure("Not found");
                pc.Open(action.Value);
                break;
            case ActionKind.Link:
                pc.Open(Links.Normalize(action.Value));
                break;
            case ActionKind.SettingsPage:
                pc.Open(action.Value);
                break;
            case ActionKind.Keys:
                await pc.PressAsync(KeyCombo.Parse(action.Value) ?? throw new ActionFailure("No keys"));
                break;
            case ActionKind.Text:
                if (action.Value.Length > 0)
                    await pc.TypeAsync(action.Value);
                break;
            case ActionKind.Command:
                if (string.IsNullOrWhiteSpace(action.Value))
                    throw new ActionFailure("No command");
                pc.Run(action.Shell, action.Value);
                break;
            case ActionKind.Media:
                pc.Media(Enum.TryParse<MediaAction>(action.Value, out var media) ? media : throw new ActionFailure("No action"));
                break;
            case ActionKind.Window:
                pc.Window(window, Enum.TryParse<WindowAction>(action.Value, out var verb) ? verb : throw new ActionFailure("No action"));
                break;
            case ActionKind.Chippi:
                var command = ChippiCommands().FirstOrDefault(c => c.Key == action.Value) ?? throw new ActionFailure("Not found");
                await command.Run();
                break;
            case ActionKind.Selection:
                await SelectionAsync(Enum.TryParse<SelectionAction>(action.Value, out var selection) ? selection : SelectionAction.Search);
                break;
        }
    }

    private async Task SelectionAsync(SelectionAction action)
    {
        var (terminal, text) = await pc.TerminalSelectionAsync();
        if (!terminal)
            text = clipboard() is { } hub ? await hub.CopySelectionAsync() : throw new ActionFailure("Clipboard unavailable");
        if (string.IsNullOrWhiteSpace(text))
            throw new ActionFailure("Nothing selected");
        switch (action)
        {
            case SelectionAction.Search:
                pc.Open(searchUrl(text.Trim()));
                break;
            case SelectionAction.OpenLink:
                pc.Open(Links.FromSelection(text) ?? throw new ActionFailure("Not a link"));
                break;
            case SelectionAction.CopyPlain:
                if (clipboard() is not { } writer || await writer.WriteAsync(ClipContent.FromText(text)) != ClipStatus.Ok)
                    throw new ActionFailure("Clipboard busy");
                break;
        }
    }
}
