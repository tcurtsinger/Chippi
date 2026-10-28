// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Clipboard.Storage;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.Shelf;

/// <summary>
/// The Shelf (design notes #37 to #50): files, text, links and pictures dropped on its island page, kept
/// until dragged out or removed. No Settings page (notes #47). Uninstalling keeps the items, so they come
/// back on reinstall.
/// </summary>
public sealed class ShelfModule : IFeatureModule
{
    public const string Id = "shelf";
    public const string FileName = "shelf.db";

    private readonly Func<string, ShelfStore> _open;
    private IIslandTile? _tile;
    private ShelfPage? _page;

    public ShelfModule() : this(path => ShelfStore.Open(path, DpapiKeyProtector.Instance)) { }

    /// <param name="open">Opens the store at a path; tests pass a fake key.</param>
    public ShelfModule(Func<string, ShelfStore> open) => _open = open;

    public FeatureDescriptor Descriptor { get; } = new(Id, "Shelf", "Holds dragged files and text while you move between apps", FeatureGroup.Files)
    {
        Icon = nameof(LucideIcons.Inbox),
        Presets = PresetMembership.PowerUser,
    };

    /// <summary>The kept items; null while stopped.</summary>
    public ShelfService? Shelf { get; private set; }

    public async Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var hub = context.Clipboard ?? throw new InvalidOperationException("No clipboard service");
        string path = Path.Combine(context.DataDirectory, FileName);
        var shelf = Shelf = new ShelfService(hub, () => _open(path), log: context.Logger);
        shelf.Changed += UpdateStatus;
        await shelf.LoadAsync(File.Exists(path)).ConfigureAwait(false);
        ShelfPage Page() => _page ??= new ShelfPage(shelf, context.HoldIslandOpen);
        _tile = context.Ui.AddIslandTile(new IslandTileSpec(Id, "Shelf", nameof(LucideIcons.Inbox), Order: 90, SectionOrder: 0, () => Page())
        {
            InitialFocus = () => Page().InitialFocus, // the first item, or the empty zone (notes #58)
            OpensOnDrag = true, // a drag over the closed tab opens the Shelf (notes #48)
        });
        UpdateStatus();
    }

    public Task StopAsync(CancellationToken ct)
    {
        if (Shelf is { } shelf)
        {
            shelf.Changed -= UpdateStatus;
            shelf.Dispose();
        }
        Shelf = null;
        _tile?.SetStatus("");
        _tile = null;
        _page = null;
        return Task.CompletedTask;
    }

    /// <summary>"No items", "1 item", "5 items" (notes #4).</summary>
    private void UpdateStatus()
    {
        int count = Shelf?.Items.Count ?? 0;
        _tile?.SetStatus(count == 0 ? "No items" : count == 1 ? "1 item" : $"{count} items");
    }
}
