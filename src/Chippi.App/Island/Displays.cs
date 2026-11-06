// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Windows.Win32.Graphics.Gdi;
using Chippi.UI.Displays;

namespace Chippi.App.Island;

internal sealed record DisplayInfo(string Id, string Name, HMONITOR Handle, bool IsMain);

/// <summary>Adapts shared monitor identities to the app's generated native handle type.</summary>
internal static class Displays
{
    public static unsafe IReadOnlyList<DisplayInfo> Connected() => DisplayCatalog.Connected()
        .Select(d => new DisplayInfo(d.Id, d.Name, new HMONITOR((void*)d.Handle), d.IsMain)).ToArray();

    public static HMONITOR? Find(string? id) => string.IsNullOrEmpty(id) ? null : Connected()
        .FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))?.Handle;

    public static unsafe HMONITOR Main => new((void*)DisplayCatalog.Main);
}
