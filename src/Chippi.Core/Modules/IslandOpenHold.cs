// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Core.Modules;

/// <summary>Overlapping dialogs and drags release only their own hold, including repeated disposal.</summary>
public sealed class IslandOpenHold
{
    private int _count;
    public bool IsHeld => Volatile.Read(ref _count) > 0;
    public IDisposable Hold() { Interlocked.Increment(ref _count); return new Lease(this); }
    private sealed class Lease(IslandOpenHold owner) : IDisposable
    {
        private IslandOpenHold? _owner = owner;
        public void Dispose() { if (Interlocked.Exchange(ref _owner, null) is { } at) Interlocked.Decrement(ref at._count); }
    }
}
