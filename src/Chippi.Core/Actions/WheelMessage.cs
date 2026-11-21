// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Actions;

/// <summary>The words of a posted WM_MOUSEWHEEL (<see cref="IActionPc.PostWheel"/>).</summary>
public static class WheelMessage
{
    public const uint Message = 0x020A;
    public const int NotchDelta = 120;
    public const int MaxNotches = 100;

    /// <summary>The window message's wParam: no keys held, the wheel delta in the high word (negative is toward the user, so down).</summary>
    public static nint WParam(int notches)
    {
        if (notches == 0 || Math.Abs(notches) > MaxNotches) throw new ArgumentOutOfRangeException(nameof(notches));
        short delta = checked((short)(-NotchDelta * notches));
        return (nint)((uint)(ushort)delta << 16);
    }

    /// <summary>The wheel delta in a wParam: positive is up, as Windows reports it.</summary>
    public static int DeltaOf(nint wParam) => (short)((ulong)wParam >> 16 & 0xFFFF);

    /// <summary>The lParam: the screen point, x in the low word and y in the high word, each a signed 16-bit value.</summary>
    public static nint LParam(int screenX, int screenY)
    {
        if (screenX is < short.MinValue or > short.MaxValue || screenY is < short.MinValue or > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(screenX));
        return (nint)(((uint)(ushort)(short)screenY << 16) | (ushort)(short)screenX);
    }

    public static (int X, int Y) PointOf(nint lParam) => ((short)((ulong)lParam & 0xFFFF), (short)((ulong)lParam >> 16 & 0xFFFF));
}
