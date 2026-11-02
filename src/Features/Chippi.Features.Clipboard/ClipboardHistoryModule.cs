// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Clipboard.Storage;
using Chippi.Core.Modules;
using Chippi.Features.Clipboard.History;
using Chippi.Features.Clipboard.Views;
using Chippi.UI.Controls;

namespace Chippi.Features.Clipboard;

/// <summary>
/// Clipboard history (Phase 5): recording off until turned on, then saved encrypted across restarts;
/// the Clipboard tile and page; Settings › Clipboard history. Uninstalling erases what's saved (Travis, task 31).
/// </summary>
public sealed class ClipboardHistoryModule : IFeatureModule
{
    public const string Id = "clipboard-history";
    private const string FileName = "history.db";

    private readonly ClipboardFeatures _features;
    private readonly string _dataDirectory;
    private ModuleContext? _context;
    private IIslandTile? _tile;
    private ClipboardView? _view;

    /// <param name="dataDirectory">This module's data folder, so uninstalling can erase it without a start.</param>
    public ClipboardHistoryModule(ClipboardFeatures features, string? dataDirectory = null)
    {
        _features = features;
        _dataDirectory = dataDirectory ?? Path.Combine(ModuleServices.DefaultDataRoot, Id);
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Clipboard history", "Searchable history of what you copied, with pinned items", FeatureGroup.ClipboardText)
    {
        Icon = nameof(LucideIcons.ClipboardList),
        Presets = PresetMembership.Essentials | PresetMembership.PowerUser,
    };

    public ClipboardHistorySettings Settings { get; private set; } = new();

    /// <summary>What it shares with Clean URL, for the Settings page's Paste.</summary>
    internal ClipboardFeatures Features => _features;

    /// <summary>The saved copies; null while stopped.</summary>
    public ClipboardHistory? History { get; private set; }

    /// <summary>Raised after the settings change, on the caller's thread.</summary>
    public event Action? Changed;

    public async Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var hub = context.Clipboard ?? throw new InvalidOperationException("No clipboard service");
        _context = context;
        Settings = context.Settings.Get<ClipboardHistorySettings>().Normalized();
        string path = Path.Combine(context.DataDirectory, FileName);
        History = new ClipboardHistory(hub, () => HistoryStore.Open(path, new DpapiKeyProtector()), _features.AutoCleaned, log: context.Logger);
        History.Changed += UpdateStatus;
        await History.LoadAsync(File.Exists(path)).ConfigureAwait(false);
        History.Apply(Settings.Recording, Settings.Paused, Settings.AppsToSkip);
        context.Ui.SetSettingsPage(() => new ClipboardHistorySettingsPage(this));
        _features.PasteTarget = context.IslandPasteTarget;
        _features.SettingsPasteTarget = context.SettingsPasteTarget;
        _features.CloseIsland = context.CloseIsland;
        _tile = context.Ui.AddIslandTile(new IslandTileSpec(ClipboardFeatures.TileKey, "Clipboard", nameof(LucideIcons.ClipboardList), Order: 70, SectionOrder: 0,
            () => View())
        {
            CreateHeader = () => View().Header,
            InitialFocus = () => View().InitialFocus, // Search, or the link field in Clean URL (notes #58)
            StatusPriority = 1, // History's status over Clean URL's (notes #4)
            HandleEscape = () => _view?.HandleEscape() == true, // the first Esc clears a search or leaves Clean URL (notes #59)
        });
        context.Ui.AddCommandSearch(Copies);
        UpdateStatus();
        _features.History = this;
    }

    /// <summary>
    /// The Command bar's clipboard rows (docs/phase-5.5/command-bar.md): the newest copies that match, pinned or not, each
    /// pasted into the app that was in front, or copied when there was none. Private copies were never saved.
    /// </summary>
    private IReadOnlyList<ChippiCommand> Copies(string text)
    {
        if (History is not { } history || text.Length < 2)
            return [];
        var now = DateTimeOffset.Now;
        return [.. history.Entries
            .Where(e => !history.Expired(e) && e.Matches(text))
            .OrderByDescending(e => e.Recent)
            .Take(8)
            .Select(e => new ChippiCommand($"clipboard/{e.Id}", Format.Line(e), nameof(LucideIcons.Clipboard), () => PasteAsync(history, e))
                { Kind = ChippiCommandKind.Paste, Detail = Copied(e.Recent, now) })];
    }

    /// <summary>"Copied today, 4:12 PM", "Copied yesterday", "Copied Oct 30".</summary>
    internal static string Copied(DateTimeOffset at, DateTimeOffset now)
    {
        var day = at.ToLocalTime().Date;
        var today = now.ToLocalTime().Date;
        return day == today ? $"Copied today, {at.ToLocalTime():t}"
            : day == today.AddDays(-1) ? "Copied yesterday"
            : $"Copied {day.ToString("MMM d", System.Globalization.CultureInfo.CurrentCulture)}";
    }

    private async Task PasteAsync(ClipboardHistory history, HistoryEntry entry)
    {
        if (_features.PasteTarget?.Invoke() is not { } target)
        {
            if (await history.CopyAsync(entry) != Core.Clipboard.ClipStatus.Ok)
                throw new InvalidOperationException("Clipboard busy");
            return;
        }
        if (await history.PasteAsync(entry, target) == Core.Clipboard.PasteOutcome.Busy)
            throw new InvalidOperationException("Clipboard busy");
    }

    private ClipboardView View() => _view ??= new ClipboardView(_features);

    public Task StopAsync(CancellationToken ct)
    {
        if (_features.History == this)
            _features.History = null;
        if (History is not null)
        {
            History.Changed -= UpdateStatus;
            History.Dispose();
            History = null;
        }
        _features.PasteTarget = null;
        _features.CloseIsland = null;
        _tile = null;
        _view = null;
        _context = null;
        return Task.CompletedTask;
    }

    /// <summary>Uninstalling erases the saved history: the file, its log and its key (notes #56).</summary>
    public Task UninstallAsync(CancellationToken ct)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var file = Path.Combine(_dataDirectory, FileName + suffix);
            if (File.Exists(file))
                File.Delete(file);
        }
        return Task.CompletedTask;
    }

    /// <summary>Whether uninstalling would erase anything, so the switch asks first (notes #56).</summary>
    public bool HasHistory => History?.Any() ?? File.Exists(Path.Combine(_dataDirectory, FileName));

    public string? UninstallQuestion() => HasHistory ? "Uninstall and erase history?" : null;

    public void Save(ClipboardHistorySettings settings)
    {
        Settings = settings.Normalized();
        _context?.Settings.Save(Settings);
        History?.Apply(Settings.Recording, Settings.Paused, Settings.AppsToSkip);
        UpdateStatus();
        Changed?.Invoke();
    }

    public void SetRecording(bool on) => Save(Settings with { Recording = on });

    public void SetPaused(bool paused) => Save(Settings with { Paused = paused });

    /// <summary>"Off", "Paused", "No items", "1 item", "23 items": never the copy itself (notes #4).</summary>
    internal static string Status(ClipboardHistorySettings settings, int count) =>
        !settings.Recording ? "Off"
        : settings.Paused ? "Paused"
        : count switch { 0 => "No items", 1 => "1 item", _ => $"{count} items" };

    private void UpdateStatus()
    {
        _tile?.SetStatus(Status(Settings, History?.Entries.Count ?? 0));
        _tile?.SetPageNote(Settings.Recording && Settings.Paused ? "Paused" : ""); // on the page's header too (notes #10)
    }
}
