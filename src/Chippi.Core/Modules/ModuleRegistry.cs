// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Permissions;
using Microsoft.Extensions.Logging;

namespace Chippi.Core.Modules;

/// <summary>A registered feature and its live state, as the hub and flyout see it.</summary>
public sealed class FeatureEntry
{
    internal FeatureEntry(FeatureDescriptor descriptor, IFeatureModule? module)
    {
        Descriptor = descriptor;
        Module = module;
    }

    public FeatureDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    /// <summary>A placeholder with no implementation yet. Shown in the hub, never installable.</summary>
    public bool IsComingSoon => Module is null;

    public bool IsInstalled { get; internal set; }

    /// <summary>Started successfully and not yet stopped.</summary>
    public bool IsRunning { get; internal set; }

    /// <summary>Why the last start failed, if it did. The feature stays installed.</summary>
    public Exception? LastError { get; internal set; }

    /// <summary>Tile, tabs and settings page the running module contributed. Empty when not running.</summary>
    public IReadOnlyList<ToolTileModel> ToolTiles => Ui?.ToolTiles ?? [];

    public IReadOnlyList<FlyoutTabContribution> FlyoutTabs => Ui?.Tabs ?? [];

    public SettingsPageContribution? SettingsPage => Ui?.SettingsPage;

    internal IFeatureModule? Module { get; }

    internal ModuleUi? Ui { get; set; }

    /// <summary>
    /// The running module's notices, for the app-wide Settings › Notifications page: its declared
    /// <see cref="Notifications.INotifier.Kinds"/>, and <see cref="Notifications.INotifier.PlacesFor"/> /
    /// <see cref="Notifications.INotifier.SetPlaces"/> for each. Null while the feature is not running.
    /// Places are saved with the module's settings, so they survive uninstall like the rest.
    /// </summary>
    public Notifications.INotifier? Notifier => RunningNotifier;

    internal Notifications.ModuleNotifier? RunningNotifier { get; set; }
}

/// <summary>The "features" section of settings.json.</summary>
internal sealed class FeatureState
{
    public const string SectionName = "features";

    public FeaturePreset Preset { get; set; } = FeaturePreset.Essentials;

    public List<string> Installed { get; set; } = [];

    /// <summary>Capabilities some installed module has needed at some point; drives the Permissions "unused" flag.</summary>
    public List<string> CapabilitiesUsed { get; set; } = [];
}

/// <summary>
/// Knows every feature, installs and uninstalls them live, applies presets, and persists the
/// installed set. All mutating calls are serialized; <see cref="Changed"/> fires after each.
/// </summary>
/// <param name="services">Data folders, activation verbs and notices for modules. Defaults suit tests.</param>
public sealed class ModuleRegistry(ConfigStore config, IslandActivityHub island, ILoggerFactory loggerFactory, ModuleServices? services = null)
{
    private readonly ModuleServices _services = services ?? new ModuleServices();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<FeatureEntry> _entries = [];
    private readonly ILogger _logger = loggerFactory.CreateLogger("Modules");
    private FeatureState _state = new();
    private bool _started;

    /// <summary>Raised after install state, running state, preset or a module's UI contributions change. May fire on any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Every registered feature, in registration order.</summary>
    public IReadOnlyList<FeatureEntry> Features => _entries;

    public FeaturePreset Preset => _state.Preset;

    public int InstalledCount => _entries.Count(e => e.IsInstalled);

    public FeatureEntry? Find(string id) => _entries.Find(e => e.Id == id);

    /// <summary>Registers a real module. Call before <see cref="StartAsync"/>.</summary>
    public void Register(IFeatureModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Add(new FeatureEntry(module.Descriptor, module));
    }

    /// <summary>Registers a "Coming soon" placeholder: listed in the hub, never installable.</summary>
    public void RegisterComingSoon(FeatureDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Add(new FeatureEntry(descriptor, module: null));
    }

    /// <summary>
    /// Reads the installed set from config and starts those modules. On first run (no
    /// "features" section) it applies the Essentials preset.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _started = true;
            await SyncFromConfigAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        RaiseChanged();
    }

    /// <summary>Re-reads the installed set after a settings import and starts or stops modules to match.</summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SyncFromConfigAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        RaiseChanged();
    }

    /// <summary>Installs or uninstalls one feature live and switches the preset to Custom.</summary>
    /// <exception cref="InvalidOperationException">The feature is a Coming-soon placeholder.</exception>
    public async Task SetInstalledAsync(string id, bool installed, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entry = Find(id) ?? throw new ArgumentException($"No feature '{id}' is registered.", nameof(id));
            if (entry.IsComingSoon && installed)
                throw new InvalidOperationException($"'{entry.Descriptor.Name}' is coming soon and cannot be installed yet.");

            if (entry.IsInstalled != installed)
                await SetInstalledCoreAsync(entry, installed, ct).ConfigureAwait(false);
            _state.Preset = FeaturePreset.Custom;
            SaveState();
        }
        finally
        {
            _gate.Release();
        }
        RaiseChanged();
    }

    /// <summary>
    /// Installs exactly the preset's features and uninstalls the rest. <see cref="FeaturePreset.Custom"/>
    /// only relabels the current set.
    /// </summary>
    public async Task ApplyPresetAsync(FeaturePreset preset, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ApplyPresetCoreAsync(preset, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        RaiseChanged();
    }

    /// <summary>Stops every running module (app exit). The installed set is kept for next launch.</summary>
    public async Task StopAllAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Every module gets stopped even if one stop is cancelled, so exit always releases
            // execution states and hooks; the cancellation is reported afterwards.
            OperationCanceledException? cancelled = null;
            foreach (var entry in _entries.Where(e => e.IsRunning).Reverse().ToList())
            {
                try
                {
                    await StopModuleAsync(entry, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex)
                {
                    cancelled ??= ex;
                }
            }
            if (cancelled is not null)
                throw cancelled;
        }
        finally
        {
            _gate.Release();
        }
        RaiseChanged();
    }

    /// <summary>The Permissions page model: each capability, the installed modules using it, and whether it is now unused.</summary>
    public IReadOnlyList<CapabilityUsage> GetPermissions() =>
        PermissionsModel.Build(_entries, _state.CapabilitiesUsed);

    /// <summary>Forgets an unused capability (the user removed the driver, say) so it stops being flagged.</summary>
    public void DismissUnusedCapability(string capabilityId)
    {
        if (_entries.Any(e => e.IsInstalled && e.Descriptor.Requires.Any(c => c.Id == capabilityId)))
            return; // still in use; nothing to dismiss
        if (_state.CapabilitiesUsed.Remove(capabilityId))
        {
            SaveState();
            RaiseChanged();
        }
    }

    /// <summary>The features a preset installs. Placeholders are never included.</summary>
    public IEnumerable<FeatureEntry> PresetMembers(FeaturePreset preset) => _entries.Where(e => !e.IsComingSoon && preset switch
    {
        FeaturePreset.Everything => true,
        FeaturePreset.Essentials => e.Descriptor.Presets.HasFlag(PresetMembership.Essentials),
        FeaturePreset.Audio => e.Descriptor.Presets.HasFlag(PresetMembership.Audio),
        FeaturePreset.PowerUser => e.Descriptor.Presets.HasFlag(PresetMembership.PowerUser),
        _ => e.IsInstalled,
    });

    private void Add(FeatureEntry entry)
    {
        if (_started)
            throw new InvalidOperationException("Register features before the registry starts.");
        if (Find(entry.Id) is not null)
            throw new ArgumentException($"A feature with id '{entry.Id}' is already registered.");
        _entries.Add(entry);
    }

    private async Task SyncFromConfigAsync(CancellationToken ct)
    {
        if (!config.HasSection(FeatureState.SectionName))
        {
            _logger.LogInformation("First run: applying the Essentials preset");
            await ApplyPresetCoreAsync(FeaturePreset.Essentials, ct).ConfigureAwait(false);
            return;
        }

        _state = config.GetSection<FeatureState>(FeatureState.SectionName);
        var wanted = _state.Installed.ToHashSet(StringComparer.Ordinal);
        foreach (var entry in _entries.Where(e => !e.IsComingSoon))
        {
            bool install = wanted.Contains(entry.Id);
            if (entry.IsInstalled != install || (install && !entry.IsRunning))
                await SetInstalledCoreAsync(entry, install, ct).ConfigureAwait(false);
        }
    }

    private async Task ApplyPresetCoreAsync(FeaturePreset preset, CancellationToken ct)
    {
        if (preset != FeaturePreset.Custom)
        {
            var members = PresetMembers(preset).ToHashSet();
            foreach (var entry in _entries.Where(e => !e.IsComingSoon))
            {
                bool install = members.Contains(entry);
                if (entry.IsInstalled != install)
                    await SetInstalledCoreAsync(entry, install, ct).ConfigureAwait(false);
            }
        }
        _state.Preset = preset;
        SaveState();
    }

    private async Task SetInstalledCoreAsync(FeatureEntry entry, bool installed, CancellationToken ct)
    {
        entry.IsInstalled = installed;
        if (installed)
        {
            foreach (var capability in entry.Descriptor.Requires)
            {
                if (!_state.CapabilitiesUsed.Contains(capability.Id))
                    _state.CapabilitiesUsed.Add(capability.Id);
            }
            if (!entry.IsRunning)
                await StartModuleAsync(entry, ct).ConfigureAwait(false);
        }
        else if (entry.IsRunning)
        {
            await StopModuleAsync(entry, ct).ConfigureAwait(false);
        }
        _logger.LogInformation("{Id} {Action}", entry.Id, installed ? "installed" : "uninstalled");
    }

    private async Task StartModuleAsync(FeatureEntry entry, CancellationToken ct)
    {
        var id = entry.Id;
        var ui = new ModuleUi(id, RaiseChanged);
        var logger = loggerFactory.CreateLogger("Module." + id);
        var publisher = island.CreatePublisher(id);
        string dataDirectory = Path.Combine(_services.DataRoot, id);
        var notifier = new Notifications.ModuleNotifier(id, config, publisher, _services, logger);
        var context = new ModuleContext(id, new ModuleSettings(config, id), logger, publisher, ui)
        {
            DataDirectory = dataDirectory,
            Notifier = notifier,
            Activation = new ActivationRouter.ModuleActivation(_services.Activation, id),
        };

        entry.Ui = ui;
        entry.RunningNotifier = notifier;
        try
        {
            // Inside the isolation boundary: an unwritable data folder fails this module only.
            Directory.CreateDirectory(dataDirectory);
            await entry.Module!.StartAsync(context, ct).ConfigureAwait(false);
            entry.IsRunning = true;
            entry.LastError = null;
        }
        catch (Exception ex)
        {
            // A start that threw or was cancelled may already hold handles, timers or an execution
            // state, so the module is always stopped (best effort, uncancellable) and its UI and
            // island entries removed. A broken module must not take the shell down: it stays
            // installed so the user can see the error in the hub and uninstall it. Cancellation
            // still propagates to the caller after the cleanup.
            if (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "{Id} failed to start", id);
                entry.LastError = ex;
            }
            try
            {
                await entry.Module!.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception stopEx)
            {
                _logger.LogError(stopEx, "{Id} failed to stop after a failed start", id);
            }
            finally
            {
                CleanUpAfterStop(entry);
            }
            if (ex is OperationCanceledException)
                throw;
        }
    }

    /// <summary>Stops one module. UI and island cleanup always runs; cancellation propagates after it.</summary>
    private async Task StopModuleAsync(FeatureEntry entry, CancellationToken ct)
    {
        try
        {
            await entry.Module!.StopAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "{Id} failed to stop cleanly", entry.Id);
        }
        finally
        {
            CleanUpAfterStop(entry);
        }
    }

    private void CleanUpAfterStop(FeatureEntry entry)
    {
        entry.IsRunning = false;
        entry.Ui?.Clear();
        entry.Ui = null;
        entry.RunningNotifier?.Dispose(); // pending island expiries belong to this lifetime only
        entry.RunningNotifier = null;
        island.ClearModule(entry.Id);
        _services.Activation.RemoveModule(entry.Id);
    }

    private void SaveState()
    {
        // Ids this build does not know (a module from a newer version) stay installed in the file.
        var unknown = _state.Installed.Where(id => Find(id) is null);
        _state.Installed = [.. _entries.Where(e => e.IsInstalled).Select(e => e.Id), .. unknown];
        config.SetSection(FeatureState.SectionName, _state);
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class ModuleSettings(ConfigStore config, string moduleId) : IModuleSettings
    {
        public T Get<T>() where T : class, new() => config.GetModuleSettings<T>(moduleId);

        public void Save<T>(T value) where T : class => config.SetModuleSettings(moduleId, value);
    }
}
