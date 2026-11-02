// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Media;
using Chippi.Core.Clipboard;
using Chippi.Core.Modules;
using Chippi.Features.CommandBar;
using Chippi.Features.RadialMenu.Pc;

namespace Chippi.Features.RadialMenu.Slices;

/// <summary>
/// Runs a slice (docs/phase-5.5/radial-menu.md, What a slice can do), and tells which slices' targets are gone. Each run
/// throws <see cref="SliceFailure"/> with the reason the bar shows.
/// </summary>
internal sealed class SliceRunner(IRadialPc pc, Func<IReadOnlyList<ChippiCommand>> commands, Func<IClipboardHub?> clipboard, Func<WebEngine> engine)
{
    /// <summary>The Chippi commands a slice may run: everything Chippi does but its clipboard copies, which come and go.</summary>
    public IReadOnlyList<ChippiCommand> ChippiCommands() => [.. Read(commands).Where(c => c.Kind != ChippiCommandKind.Paste)];

    private static IReadOnlyList<ChippiCommand> Read(Func<IReadOnlyList<ChippiCommand>> commands)
    {
        try { return commands(); }
        catch (Exception) { return []; }
    }

    /// <summary>
    /// Whether the slice's target is gone: an app no longer installed, a file or program moved, a Chippi command whose
    /// feature isn't running. The Start menu's apps count only once they've been read.
    /// </summary>
    public bool IsMissing(Slice slice, IReadOnlyList<ChippiCommand>? known = null) => slice.Kind switch
    {
        SliceKind.App when Path.IsPathFullyQualified(slice.Value) => !pc.Exists(slice.Value),
        SliceKind.App => pc.Apps.Count > 0 && !pc.Apps.Any(a => string.Equals(a.ParsingName, slice.Value, StringComparison.OrdinalIgnoreCase)),
        SliceKind.File => !pc.Exists(slice.Value),
        SliceKind.Chippi => !(known ?? ChippiCommands()).Any(c => c.Key == slice.Value),
        _ => false,
    };

    /// <summary>The app's or file's own icon, for an App or File slice without a picked glyph; null for any other.</summary>
    public Task<ImageSource?> IconAsync(Slice slice, int pixels)
    {
        if (slice.Glyph is not null || slice.Kind is not (SliceKind.App or SliceKind.File) || slice.Value.Length == 0)
            return Task.FromResult<ImageSource?>(null);
        return pc.IconAsync(slice.Value, pixels);
    }

    /// <param name="window">The window that was under the pointer when the wheel opened, for a Window slice.</param>
    public async Task RunAsync(Slice slice, nint window)
    {
        switch (slice.Kind)
        {
            case SliceKind.App:
                pc.Open(Path.IsPathFullyQualified(slice.Value) ? slice.Value : @"shell:AppsFolder\" + slice.Value);
                break;
            case SliceKind.File:
                if (!pc.Exists(slice.Value))
                    throw new SliceFailure("Not found");
                pc.Open(slice.Value);
                break;
            case SliceKind.Link:
                pc.Open(Links.Normalize(slice.Value));
                break;
            case SliceKind.SettingsPage:
                pc.Open(slice.Value);
                break;
            case SliceKind.Keys:
                await pc.PressAsync(KeyCombo.Parse(slice.Value) ?? throw new SliceFailure("No keys"));
                break;
            case SliceKind.Text:
                if (slice.Value.Length > 0)
                    await pc.TypeAsync(slice.Value);
                break;
            case SliceKind.Command:
                if (string.IsNullOrWhiteSpace(slice.Value))
                    throw new SliceFailure("No command");
                pc.Run(slice.Shell, slice.Value);
                break;
            case SliceKind.Media:
                pc.Media(Enum.TryParse<MediaAction>(slice.Value, out var media) ? media : throw new SliceFailure("No action"));
                break;
            case SliceKind.Window:
                pc.Window(window, Enum.TryParse<WindowAction>(slice.Value, out var action) ? action : throw new SliceFailure("No action"));
                break;
            case SliceKind.Chippi:
                var command = ChippiCommands().FirstOrDefault(c => c.Key == slice.Value) ?? throw new SliceFailure("Not found");
                await command.Run();
                break;
            case SliceKind.Selection:
                await SelectionAsync(Enum.TryParse<SelectionAction>(slice.Value, out var selection) ? selection : SelectionAction.Search);
                break;
        }
    }

    private async Task SelectionAsync(SelectionAction action)
    {
        var (terminal, text) = await pc.TerminalSelectionAsync();
        if (!terminal)
            text = clipboard() is { } hub ? await hub.CopySelectionAsync() : throw new SliceFailure("Clipboard unavailable");
        if (string.IsNullOrWhiteSpace(text))
            throw new SliceFailure("Nothing selected");
        switch (action)
        {
            case SelectionAction.Search:
                pc.Open(engine().SearchUrl(text.Trim()));
                break;
            case SelectionAction.OpenLink:
                pc.Open(Links.FromSelection(text) ?? throw new SliceFailure("Not a link"));
                break;
            case SelectionAction.CopyPlain:
                if (clipboard() is not { } writer || await writer.WriteAsync(ClipContent.FromText(text)) != ClipStatus.Ok)
                    throw new SliceFailure("Clipboard busy");
                break;
        }
    }
}
