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
    public ScratchpadModule() : this(c => new ScratchpadStore(Path.Combine(c.DataDirectory, "notes.db"), DpapiKeyProtector.Instance)) { }
    public ScratchpadModule(Func<ModuleContext, IScratchpadStore> open) => _open = open;
    public FeatureDescriptor Descriptor { get; } = new("scratchpad", "Scratchpad", "A quick notes pad that saves as you type", FeatureGroup.ClipboardText)
    {
        Icon = "NotebookPen", Presets = PresetMembership.PowerUser,
        Badges = [FeatureBadge.Overlap("Overlaps Notepad")],
    };
    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _session = new ScratchpadSession(() => _open(context));
        ScratchpadPage Page() => _page ??= new ScratchpadPage(_session, context.HoldIslandOpen);
        context.Ui.AddIslandTile(new IslandTileSpec("scratchpad", "Scratchpad", "NotebookPen", 80, 0, () => Page())
        {
            PageWidth = 440, CreateHeader = () => Page().Header, InitialFocus = () => Page().InitialFocus,
        });
        return Task.CompletedTask;
    }
    public async Task StopAsync(CancellationToken ct)
    {
        _page?.Dispose(); _page = null;
        if (_session is { } session) await session.DisposeAsync().ConfigureAwait(false);
        _session = null;
    }
}
