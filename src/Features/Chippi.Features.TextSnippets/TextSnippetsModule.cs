// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Automation;
using Chippi.Core.Diagnostics;
using Chippi.Core.Input;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.TextSnippets;

/// <summary>
/// Text snippets (docs/phase-6/text-snippets.md): a short trigger typed anywhere becomes its text, typed out as keystrokes
/// and never pasted. Expansion while typing is a filter in the input hub, in only while its switch is on (off until the
/// owner turns it on, even once installed). Every snippet is also a Chippi command, so Search lists it by name and trigger
/// and any Shortcuts door can fire it; those two ways work with the switch off.
/// </summary>
public sealed class TextSnippetsModule : IFeatureModule
{
    public const string Id = "text-snippets";
    public const string NotInserted = "Snippet not inserted";
    public const string AdminWindow = "Not working in admin windows";
    public static readonly string[] SystemSounds = ["Asterisk", "Beep", "Exclamation", "Hand", "Question"];

    private readonly object _gate = new();
    private readonly ISnippetSender _sender;
    private readonly IKeyReader _reader;
    private readonly Func<bool> _passwordFocused;
    private readonly SnippetMatcher _matcher = new();
    private ModuleContext? _context;
    private SnippetFilter? _filter;
    private IInputRegistration? _registration;
    private SnippetExpander? _expander;

    internal TextSnippetsModule(ISnippetSender sender, IKeyReader reader, Func<bool> passwordFocused)
    {
        _sender = sender;
        _reader = reader;
        _passwordFocused = passwordFocused;
    }

    public TextSnippetsModule() : this(new WindowsSnippetSender(), new WindowsKeyReader(), FocusedFieldIsPassword)
    {
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Text snippets", "Type a short abbreviation and it expands into saved text", FeatureGroup.ClipboardText)
    { Icon = nameof(LucideIcons.AlignLeft), Requires = [Capability.InputHooks], Presets = PresetMembership.PowerUser, PausesInFullscreen = true };

    public TextSnippetsSettings Settings { get; private set; } = new();

    /// <summary>"Watching the keyboard failed" while the hook is down, else null.</summary>
    public string? Error { get; private set; }

    /// <summary>The window in front is above Chippi, so nothing is seen or expanded there ("Not working in admin windows").</summary>
    public bool ForegroundElevated => _context?.Input?.ForegroundElevated == true;

    public event Action? Changed;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        lock (_gate)
        {
            _context = context;
            Settings = Normalize(context.Settings.Get<TextSnippetsSettings>());
            _expander = new SnippetExpander(_sender, () => ReadClipboardAsync(context), _passwordFocused, expanded: PlayExpandedSound, foreground: _reader.Foreground);
            _matcher.SetSnippets(Settings.Snippets);
            context.Ui.AddCommands(Commands);
            context.Ui.SetSettingsPage(_ => new Views.SnippetsPage(this));
            Apply();
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            _registration?.Dispose();
            _registration = null;
            _filter = null;
            _expander = null;
            _matcher.Reset();
            _context = null;
        }
        return Task.CompletedTask;
    }

    public void Update(Func<TextSnippetsSettings, TextSnippetsSettings> change)
    {
        lock (_gate)
        {
            if (_context is null)
                return;
            var next = Normalize(change(Settings));
            _context.Settings.Save(next);
            Settings = next;
            _matcher.SetSnippets(next.Snippets);
            Apply();
        }
        Changed?.Invoke();
    }

    /// <summary>One command per snippet, found in Search by name and trigger, and listed for Shortcuts' Insert snippet action.</summary>
    private IReadOnlyList<ChippiCommand> Commands() =>
    [.. Settings.Snippets.Where(s => s.Enabled && s.Trigger.Length > 0).Select(s =>
        new ChippiCommand("text-snippets/" + s.Id, string.IsNullOrWhiteSpace(s.Name) ? s.Trigger : s.Name, nameof(LucideIcons.AlignLeft), () => InsertAsync(s.Id))
        { Words = s.Trigger + " snippet text", Detail = "Snippet · " + s.Trigger, Kind = ChippiCommandKind.Insert })];

    /// <summary>
    /// Types a snippet into the window that was in front before Search opened, or into the one in front when no island took
    /// the keyboard (a Shortcuts door). It never waits on Always on: it was chosen on purpose. Nothing is typed when that
    /// window is gone, changed owner, or is above Chippi.
    /// </summary>
    public async Task InsertAsync(string id)
    {
        ModuleContext context;
        Snippet snippet;
        lock (_gate)
        {
            context = _context ?? throw new InvalidOperationException(NotInserted);
            snippet = Settings.Snippets.FirstOrDefault(s => s.Id == id) ?? throw new InvalidOperationException(NotInserted);
        }
        if (context.IslandPasteTarget() is { } target)
        {
            if (!Native.IsWindow(target.Window) || Native.ProcessOf(target.Window) != target.ProcessId)
                throw new InvalidOperationException(NotInserted);
            if (Native.IsElevated(target.ProcessId))
                throw new InvalidOperationException(AdminWindow);
            if (!await BroughtBackAsync(target.Window))
                throw new InvalidOperationException(NotInserted);
        }
        else if (context.Input?.ForegroundElevated == true)
            throw new InvalidOperationException(AdminWindow);
        nint window = _reader.Foreground(); // the destination: the clipboard read below can take a second
        string text = await SnippetRenderer.RenderAsync(snippet.Text, DateTimeOffset.Now, () => ReadClipboardAsync(context));
        if (!await _sender.SendAsync(0, text, null, () => _reader.Foreground() == window))
            throw new InvalidOperationException(NotInserted);
        PlayExpandedSound();
    }

    private static async Task<bool> BroughtBackAsync(nint window)
    {
        for (int i = 0; i < 20; i++)
        {
            if (Native.Activate(window))
                return true;
            await Task.Delay(15);
        }
        return false;
    }

    private static async Task<string?> ReadClipboardAsync(ModuleContext context) =>
        context.Clipboard is { } hub ? (await hub.ReadAsync()).Content?.Text : null;

    private static TextSnippetsSettings Normalize(TextSnippetsSettings s) => s with
    {
        Snippets = [.. (s.Snippets ?? []).Where(x => x is not null)],
        IgnoredApps = [.. (s.IgnoredApps ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase)],
        Sound = s.Sound is { } name && SystemSounds.Contains(name) ? name : null,
    };

    private void Apply()
    {
        var hub = _context?.Input;
        if (hub is null)
            return;
        if (Settings.ExpandWhileTyping)
        {
            if (_registration is null)
            {
                var expander = _expander!;
                _filter = new SnippetFilter(hub, _matcher, expander.Enqueue, _reader, expander.Cancel);
                Error = Try(() => _registration = hub.Add(_filter));
                if (Error is not null)
                    _filter = null;
            }
            _filter?.SetIgnoredApps(Settings.IgnoredApps);
        }
        else
        {
            _registration?.Dispose();
            _registration = null;
            _filter = null;
            _matcher.Reset();
            Error = null;
        }
    }

    private static string? Try(Action add)
    {
        try
        {
            add();
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn("Text snippets: the keyboard hook failed", ex);
            return "Watching the keyboard failed";
        }
    }

    private void PlayExpandedSound()
    {
        var settings = Settings;
        if (!settings.SoundOnExpand)
            return;
        try
        {
            (settings.Sound switch
            {
                "Beep" => System.Media.SystemSounds.Beep,
                "Exclamation" => System.Media.SystemSounds.Exclamation,
                "Hand" => System.Media.SystemSounds.Hand,
                "Question" => System.Media.SystemSounds.Question,
                _ => System.Media.SystemSounds.Asterisk,
            }).Play();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn("Text snippets: playing the sound failed", ex);
        }
    }

    /// <summary>
    /// The field with the keyboard is a password box, where the app says so to UI Automation. Not every app does: unknown
    /// counts as not a password. Called off the hub's thread.
    /// </summary>
    private static bool FocusedFieldIsPassword()
    {
        try
        {
            var cache = new CacheRequest { TreeScope = TreeScope.Element, TreeFilter = Automation.RawViewCondition, AutomationElementMode = AutomationElementMode.None };
            cache.Add(AutomationElement.IsPasswordProperty);
            using (cache.Activate()) return AutomationElement.FocusedElement?.Cached.IsPassword == true;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { return false; }
    }
}
