// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Clipboard;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.CommandBar.Views;
using Chippi.Features.CommandBar.Windows;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;

namespace Chippi.Features.CommandBar;

/// <summary>
/// The Command bar (docs/phase-5.5/command-bar.md): Ctrl+Alt+Space or the home's Search, a few letters, Enter. It opens
/// apps, windows, files and Windows Settings pages, runs anything Chippi does (with a value), pastes an old copy, answers a
/// sum or a conversion, copies an emoji or searches the web.
/// </summary>
public sealed class CommandBarModule : IFeatureModule
{
    public const string Id = "command-bar";

    /// <summary>The island tile's key: the home's Search, the tenth button.</summary>
    public const string TileKey = "command-bar";

    /// <summary>Its own command, for the Radial menu's Chippi slices; the Command bar leaves it out of its rows.</summary>
    public const string OpenCommand = "command-bar/open";

    private readonly Func<IPc> _createPc;
    private readonly Func<IMicHotkey> _createHotkey;
    private IMicHotkey? _hotkey;
    private IPc? _pc;
    private ModuleContext? _context;
    private CommandBarPage? _page;
    private bool _capturing;

    public CommandBarModule() : this(() => new WindowsPc(), GlobalMicHotkey.Create) { }

    public CommandBarModule(Func<IPc> pc, Func<IMicHotkey> hotkey)
    {
        _createPc = pc;
        _createHotkey = hotkey;
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Command bar", "Type to launch apps, run commands and reach any Chippi feature", FeatureGroup.Windows)
    {
        Icon = nameof(LucideIcons.Search),
        Presets = PresetMembership.PowerUser,
    };

    public CommandBarSettings Settings { get; private set; } = new();

    /// <summary>Why the shortcut didn't register ("Shortcut in use"), or null.</summary>
    public string? ShortcutError { get; private set; }

    /// <summary>The settings changed. Raised on the UI thread.</summary>
    public event Action? Changed;

    /// <summary>The page's search; null while stopped.</summary>
    internal CommandSearch? Search { get; private set; }

    internal IPc? Pc => _pc;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        _capturing = false;
        ShortcutError = null;
        Settings = context.Settings.Get<CommandBarSettings>();
        if (Settings.Shortcut is { IsValid: false })
            Settings = Settings with { Shortcut = CommandBarSettings.DefaultShortcut };
        _pc = _createPc();
        Search = new CommandSearch(_pc, () => [.. context.Commands().Where(c => c.Key != OpenCommand)], context.SearchCommands, () => Settings,
            new Uses(Path.Combine(context.DataDirectory, "uses.json")), CopyAsync);
        context.Ui.AddCommands(() => [new ChippiCommand(OpenCommand, Descriptor.Name, nameof(LucideIcons.Search), () =>
        {
            Summon();
            return Task.CompletedTask;
        }) { Words = "search launcher run type" }]);
        // No status: Search is a door to its page, which takes the keyboard as it opens.
        context.Ui.AddIslandTile(new IslandTileSpec(TileKey, "Search", nameof(LucideIcons.Search), Order: 110, SectionOrder: 0, () => Page())
        {
            CreateHeader = () => Page().Header,
            InitialFocus = () => Page().Field,
            HandleEscape = () => _page?.Escape() == true,
            TakesKeyboard = true,
            HeaderHeight = 32,
        });
        context.Ui.SetSettingsPage(section => section == "shortcut-list" ? new ShortcutRow(this, Descriptor.Name) : new CommandBarSettingsPage(this));
        if (Settings.Shortcut is { } shortcut)
            TryRegister(shortcut);
        _pc.RefreshApps(); // read once now, so the first open already has them
        return Task.CompletedTask;
    }

    private CommandBarPage Page() => _page ??= new CommandBarPage(this);

    public Task StopAsync(CancellationToken ct)
    {
        _hotkey?.Dispose();
        _hotkey = null;
        _pc?.Dispose();
        _pc = null;
        Search = null;
        _page = null;
        _context = null;
        return Task.CompletedTask;
    }

    /// <summary>After a row ran: the panel closes, unless the row moved it to another of the island's pages.</summary>
    internal void Close() => _context?.CloseIsland();

    /// <summary>Copies an answer or an emoji as a new copy, through the clipboard hub. Throws when it can't ("Copy failed").</summary>
    private async Task CopyAsync(string text)
    {
        if (_context?.Clipboard is not { } clipboard)
            throw new InvalidOperationException("Clipboard unavailable");
        if (await clipboard.WriteAsync(ClipContent.FromText(text)) is not ClipStatus.Ok and var status)
            throw new InvalidOperationException($"Copy failed: {status}");
    }

    // Settings › Command bar.

    public void SetWebSearch(WebEngine engine) => Save(Settings with { WebSearch = engine });

    public void SetOn(ResultSwitch kind, bool on) =>
        Save(Settings with { Off = [.. Enum.GetValues<ResultSwitch>().Where(k => k == kind ? !on : Settings.Off.Contains(k))] });

    /// <summary>The eye-off button or Shift+Delete: the result never shows again until Settings › Command bar › Hidden brings it back.</summary>
    public void Hide(Result result)
    {
        if (!result.CanHide || Settings.Hidden.Exists(h => h.Key == result.Key))
            return;
        Save(Settings with { Hidden = [.. Settings.Hidden, new HiddenResult(result.Key, result.Name, result.HiddenAs)] });
    }

    /// <summary>Hidden's Show.</summary>
    public void Unhide(string key) => Save(Settings with { Hidden = [.. Settings.Hidden.Where(h => h.Key != key)] });

    private void Save(CommandBarSettings settings)
    {
        _context?.Settings.Save(settings);
        Settings = settings;
        Changed?.Invoke();
    }

    // The shortcut: Ctrl+Alt+Space by default (Travis's pick), changeable in Settings › Shortcuts.

    private void Summon()
    {
        if (_capturing)
            return;
        _context?.SummonIslandPage(TileKey);
    }

    public bool ChangeShortcut(MicShortcut shortcut)
    {
        if (_context is null)
            return false;
        if (!shortcut.IsValid)
        {
            ShortcutError = "Shortcut unavailable";
            Changed?.Invoke();
            return false;
        }
        if (!TryRegister(shortcut))
        {
            Changed?.Invoke();
            return false;
        }
        Save(Settings with { Shortcut = shortcut });
        return true;
    }

    public void RemoveShortcut()
    {
        if (_context is null)
            return;
        _hotkey?.Dispose();
        _hotkey = null;
        ShortcutError = null;
        Save(Settings with { Shortcut = null });
    }

    /// <summary>While Settings records a shortcut, pressing ours doesn't open the Command bar.</summary>
    public void CaptureShortcut(bool capture) => _capturing = capture;

    private bool TryRegister(MicShortcut shortcut)
    {
        try
        {
            _hotkey ??= _createHotkey();
            ShortcutError = _hotkey.Register(shortcut, Summon) ? null : "Shortcut in use";
        }
        catch (Exception ex)
        {
            Log.Warn("Command bar: registering the shortcut failed", ex);
            ShortcutError = "Shortcut unavailable";
        }
        return ShortcutError is null;
    }
}
