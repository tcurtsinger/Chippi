// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.QuickActions;

/// <summary>The apps that refused a reviewed bulk Quit, after all other reviewed apps were asked to close.</summary>
public sealed class QuitAppsException(IReadOnlyList<OpenApp> apps, IEnumerable<Exception> errors)
    : AggregateException("Quit failed", errors)
{
    public IReadOnlyList<OpenApp> Apps { get; } = apps.ToArray();
}
