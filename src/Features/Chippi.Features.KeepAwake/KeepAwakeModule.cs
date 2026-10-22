// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.KeepAwake;

public sealed class KeepAwakeModule : IFeatureModule
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly IExecutionState _native;
    private AwakeSession? _session;
    private ModuleContext? _context;
    public KeepAwakeSettings Settings { get; private set; } = new();
    public ToolTileModel Tile { get; }
    public string? Error { get; private set; }
    public event Action? Changed;

    public KeepAwakeModule() : this(new WindowsExecutionState()) { }

    public KeepAwakeModule(IExecutionState native)
    {
        _native = native;
        Tile = new("Keep awake", nameof(LucideIcons.Coffee), ToggleAsync);
    }

    public FeatureDescriptor Descriptor { get; } = new("keep-awake", "Keep awake", "Prevent sleep while you are away", FeatureGroup.System)
    {
        Icon = nameof(LucideIcons.Coffee),
        Presets = PresetMembership.Essentials | PresetMembership.PowerUser,
        Badges = [FeatureBadge.Overlap("Overlaps PowerToys")],
    };

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        Settings = context.Settings.Get<KeepAwakeSettings>();
        try { Settings.Validate(); }
        catch (ArgumentException) { Settings = new(); }
        Error = null;
        Tile.IsOn = false;
        Tile.StateLine = "Off";
        CreateSession();
        context.Ui.AddToolTile(Tile);
        context.Ui.SetSettingsPage(() => new KeepAwakePage(this));
        return Task.CompletedTask;
    }

    public async Task ApplyAsync(KeepAwakeSettings settings, CancellationToken ct = default)
    {
        settings.Validate();
        await _gate.WaitAsync(ct);
        try
        {
            if (_context is null || _session is null) throw new InvalidOperationException("Keep awake is uninstalled.");
            _context.Settings.Save(settings);
            Settings = settings;
            if (Tile.IsOn) await _session.StartAsync(settings);
            Changed?.Invoke();
        }
        finally { _gate.Release(); }
    }

    public async Task ToggleAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_session is null) throw new InvalidOperationException("Keep awake is uninstalled.");
            if (_session.HasFailed)
            {
                // Let the failed worker finish releasing its request before a new one starts.
                await _session.DisposeAsync();
                _session.Changed -= OnSessionChanged;
                CreateSession();
            }
            Error = null;
            if (Tile.IsOn) await _session.StopAsync();
            else await _session.StartAsync(Settings);
        }
        finally { _gate.Release(); }
    }

    private void CreateSession()
    {
        _session = new AwakeSession(_native);
        _session.Changed += OnSessionChanged;
    }

    private void OnSessionChanged(bool active, DateTimeOffset? deadline, bool timed, Exception? error)
    {
        Error = error?.Message;
        Tile.IsOn = active;
        Tile.StateLine = active ? deadline is { } end ? $"Until {end.LocalDateTime:t}" : "Indefinitely" : "Off";
        if (active)
            _context?.Island.Publish(new IslandActivity("awake", IslandActivityKind.Status, "Keep awake", Tile.StateLine));
        else _context?.Island.Clear("awake");
        if (timed) _context?.Island.ReportSuccess("Keep awake finished");
        if (error is not null) _context?.Logger.LogError(error, "Keep awake execution state failed");
        Changed?.Invoke();
    }

    public void ReleaseOnExit()
    {
        // The worker never waits for the dispatcher; this also covers direct Application.Shutdown.
        _session?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async Task StopAsync(CancellationToken ct)
    {
        // Cleanup is intentionally uncancellable: uninstall and exit must release the request.
        await _gate.WaitAsync();
        try
        {
            if (_session is not null)
            {
                await _session.DisposeAsync();
                _session.Changed -= OnSessionChanged;
                _session = null;
            }
            Tile.IsOn = false;
            Tile.StateLine = "Off";
            _context?.Island.Clear("awake");
            _context = null;
            Changed?.Invoke();
        }
        finally { _gate.Release(); }
    }
}
