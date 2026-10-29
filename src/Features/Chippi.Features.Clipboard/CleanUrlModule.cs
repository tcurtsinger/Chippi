// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Modules;
using Chippi.Features.Clipboard.CleanUrl;
using Chippi.Features.Clipboard.Views;
using Chippi.UI.Controls;

namespace Chippi.Features.Clipboard;

/// <summary>
/// Clean URL (Phase 5): the manual Clean behind the Clipboard header's link button, or the whole
/// Clipboard page without Clipboard history (design notes #23); automatic cleaning, off by default;
/// Settings › Clean URL.
/// </summary>
public sealed class CleanUrlModule : IFeatureModule
{
    public const string Id = "clean-url";

    private readonly ClipboardFeatures _features;
    private ModuleContext? _context;
    private IIslandTile? _tile;
    private CleanUrlSection? _section;

    public CleanUrlModule(ClipboardFeatures features) => _features = features;

    public FeatureDescriptor Descriptor { get; } = new(Id, "Clean URL", "Strips tracking parameters from links you copy", FeatureGroup.ClipboardText)
    {
        Icon = nameof(LucideIcons.Link),
        Presets = PresetMembership.Essentials | PresetMembership.PowerUser,
    };

    public CleanUrlSettings Settings { get; private set; } = new();

    /// <summary>Automatic cleaning; null while stopped.</summary>
    public AutoCleaner? Cleaner { get; private set; }

    /// <summary>The shared clipboard, for the page's prefill and Copy.</summary>
    internal Core.Clipboard.IClipboardHub? Hub => _context?.Clipboard;

    /// <summary>Clipboard history while it runs, so a cleaned link Copy writes is listed (notes #26).</summary>
    internal ClipboardHistoryModule? History => _features.History;

    public event Action? Changed;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var hub = context.Clipboard ?? throw new InvalidOperationException("No clipboard service");
        _context = context;
        Settings = context.Settings.Get<CleanUrlSettings>().Normalized();
        Cleaner = new AutoCleaner(hub, context.Logger);
        Cleaner.Apply(Settings.Automatic, Settings.SitesToSkip);
        context.Ui.SetSettingsPage(() => new CleanUrlSettingsPage(this));
        // Shares Clipboard history's tile; its page is Clean URL alone while History isn't running.
        _tile = context.Ui.AddIslandTile(new IslandTileSpec(ClipboardFeatures.TileKey, "Clipboard", nameof(LucideIcons.ClipboardList), Order: 70, SectionOrder: 1,
            () => _section ??= new CleanUrlSection(_features))
        {
            InitialFocus = () => _section?.InitialFocus,
        });
        _features.Changed += UpdateStatus;
        _features.CleanUrl = this;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _features.Changed -= UpdateStatus;
        if (_features.CleanUrl == this)
            _features.CleanUrl = null;
        Cleaner?.Dispose();
        Cleaner = null;
        _tile = null;
        _section = null;
        _context = null;
        return Task.CompletedTask;
    }

    public void Save(CleanUrlSettings settings)
    {
        Settings = settings.Normalized();
        _context?.Settings.Save(Settings);
        Cleaner?.Apply(Settings.Automatic, Settings.SitesToSkip);
        UpdateStatus();
        Changed?.Invoke();
    }

    /// <summary>
    /// "Manual", or "Automatic" while automatic cleaning is on (notes #4). History's status wins on the
    /// tile; on the page's header it shows only while the page is Clean URL alone (notes #23).
    /// </summary>
    private void UpdateStatus()
    {
        string status = Settings.Automatic ? "Automatic" : "Manual";
        _tile?.SetStatus(status);
        _tile?.SetPageNote(_features.History is null ? status : "");
    }
}
