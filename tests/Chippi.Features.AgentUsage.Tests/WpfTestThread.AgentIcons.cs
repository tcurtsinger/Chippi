// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media.Imaging;
using Chippi.Features.AgentUsage;

namespace Chippi.Island.Tests;

internal static partial class WpfTestThread
{
    static partial void Prepare() => AgentIcons.UseLoaderForTests(_ => Task.FromResult<BitmapSource?>(null));
}
