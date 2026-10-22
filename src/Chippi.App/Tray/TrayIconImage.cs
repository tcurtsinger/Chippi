// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.App.Tray;

/// <summary>
/// Builds the tray HICON from the frames make_icon.py embedded (<see cref="ChippiIconData"/>):
/// the exact frame for the requested size, else the next larger one scaled down.
/// </summary>
internal static class TrayIconImage
{
    private static readonly byte[] Ico = Convert.FromBase64String(ChippiIconData.TrayIcoBase64);

    public static unsafe HICON Create(int size)
    {
        // ICONDIR (6 bytes), then one 16-byte ICONDIRENTRY per frame: width, height, colors,
        // reserved, planes, bit count, byte count, offset. A width byte of 0 means 256.
        int count = BitConverter.ToUInt16(Ico, 4);
        int best = -1, bestWidth = int.MaxValue, largest = 0, largestWidth = 0;
        for (int i = 0; i < count; i++)
        {
            int width = Ico[6 + (16 * i)] is 0 ? 256 : Ico[6 + (16 * i)];
            if (width >= size && width < bestWidth)
                (best, bestWidth) = (i, width);
            if (width > largestWidth)
                (largest, largestWidth) = (i, width);
        }
        if (best < 0)
            best = largest;

        int entry = 6 + (16 * best);
        int length = BitConverter.ToInt32(Ico, entry + 8);
        int offset = BitConverter.ToInt32(Ico, entry + 12);
        fixed (byte* bits = &Ico[offset])
        {
            // 0x00030000: the icon resource format version CreateIconFromResourceEx expects.
            return PInvoke.CreateIconFromResourceEx(bits, (uint)length, true, 0x00030000, size, size, IMAGE_FLAGS.LR_DEFAULTCOLOR);
        }
    }
}
