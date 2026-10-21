// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Modules;

/// <summary>
/// A feature the user can install and uninstall. The registry calls <see cref="StartAsync"/>
/// when the feature is installed (or at launch if it already was) and <see cref="StopAsync"/>
/// on uninstall and app exit.
/// </summary>
/// <remarks>
/// The constructor must not acquire anything: an uninstalled module is constructed so the
/// hub can read its <see cref="Descriptor"/>, but it is never started. Take hooks, timers,
/// handles and execution states in <see cref="StartAsync"/> and release all of them in
/// <see cref="StopAsync"/>. Island activities and UI contributions made through the
/// <see cref="ModuleContext"/> are removed by the registry after stop.
/// </remarks>
public interface IFeatureModule
{
    FeatureDescriptor Descriptor { get; }

    Task StartAsync(ModuleContext context, CancellationToken ct);

    Task StopAsync(CancellationToken ct);
}
