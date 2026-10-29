// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Clipboard.Storage;
using Chippi.Core.Modules;

namespace Chippi.Features.Scratchpad;

public sealed class ScratchpadModule : IFeatureModule
{
    private readonly Func<ModuleContext, IScratchpadStore> _open;
    private ScratchpadSession? _session;
    private ScratchpadPage? _page;
    private IIslandTile? _tile;
    public ScratchpadModule() : this(c => new ScratchpadStore(Path.Combine(c.DataDirectory, "notes.db"), DpapiKeyProtector.Instance)) { }
    public ScratchpadModule(Func<ModuleContext, IScratchpadStore> open) => _open = open;
    public FeatureDescriptor Descriptor { get; } = new("scratchpad", "Scratchpad", "A quick notes pad that saves as you type", FeatureGroup.ClipboardText)
    {
        Icon = "NotebookPen", Presets = PresetMembership.PowerUser,
    };
    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _session = new ScratchpadSession(() => _open(context));
        ScratchpadPage Page() => _page ??= new ScratchpadPage(_session, context.HoldIslandOpen);
        _tile = context.Ui.AddIslandTile(new IslandTileSpec("scratchpad", "Scratchpad", "NotebookPen", 80, 0, () => Page())
        {
            CreateHeader = () => Page().Header, InitialFocus = () => Page().InitialFocus,
        });
        var shared = _session; // Settings and the island page edit the same notes
        context.Ui.SetSettingsPage(() => new ScratchpadSettingsPage(shared));
        _session.Changed += UpdateTile;
        return _session.LoadAsync();
    }
    private void UpdateTile(ScratchpadState state)
    {
        var count = state.Document?.Notes.Count;
        _tile?.SetStatus(count is null ? "" : count == 1 ? "1 note" : $"{count} notes");
    }
    public async Task StopAsync(CancellationToken ct)
    {
        if (_session is { } active) active.Changed -= UpdateTile;
        _tile?.SetStatus(""); _tile = null;
        _page?.Dispose(); _page = null;
        if (_session is { } session) await session.DisposeAsync().ConfigureAwait(false);
        _session = null;
    }
}
