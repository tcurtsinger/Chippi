// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Actions;

public readonly record struct LaunchedApp(uint ProcessId, long Started);
