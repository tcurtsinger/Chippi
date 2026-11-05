// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.AppSwitcher;

/// <summary>
/// The order windows were last in front, from the foreground event (architecture §6). Windows that haven't been in front
/// since Chippi started keep their z-order behind the ones that have. Thread-safe: the event arrives on the UI thread,
/// and the order is read there too, but tests feed it from anywhere.
/// </summary>
public sealed class RecentWindows
{
    /// <summary>Enough for any desktop; the oldest drop off.</summary>
    public const int Capacity = 256;

    private readonly object _gate = new();
    private readonly LinkedList<nint> _order = new();
    private readonly Dictionary<nint, LinkedListNode<nint>> _nodes = [];

    /// <summary>The window came to the front.</summary>
    public void Activated(nint hwnd)
    {
        if (hwnd == 0)
            return;
        lock (_gate)
        {
            if (_nodes.TryGetValue(hwnd, out var node))
                _order.Remove(node);
            else if (_order.Count >= Capacity && _order.Last is { } last)
            {
                _order.RemoveLast();
                _nodes.Remove(last.Value);
            }
            _nodes[hwnd] = _order.AddFirst(hwnd);
        }
    }

    /// <summary>The windows in <paramref name="zOrder"/> (front first), most recently in front first.</summary>
    public IReadOnlyList<SwitcherWindow> Order(IReadOnlyList<SwitcherWindow> zOrder)
    {
        Dictionary<nint, int> rank;
        lock (_gate)
        {
            rank = new Dictionary<nint, int>(_nodes.Count);
            int i = 0;
            foreach (var hwnd in _order)
                rank[hwnd] = i++;
        }
        // A stable sort: windows never in front keep their z-order, after the rest.
        return [.. zOrder.Select((w, z) => (w, z)).OrderBy(x => rank.TryGetValue(x.w.Handle, out int r) ? r : int.MaxValue).ThenBy(x => x.z).Select(x => x.w)];
    }
}
