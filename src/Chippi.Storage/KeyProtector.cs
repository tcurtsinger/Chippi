// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Chippi.Clipboard.Storage;

/// <summary>Wraps a store's key at rest.</summary>
public interface IKeyProtector
{
    byte[] Protect(byte[] key);

    /// <summary>Throws <see cref="CryptographicException"/> when the key can't be unwrapped.</summary>
    byte[] Unprotect(byte[] wrapped);
}

/// <summary>DPAPI for the current user, no UI (CryptProtectData with CRYPTPROTECT_UI_FORBIDDEN). About 0.2 ms for a key.</summary>
public sealed class DpapiKeyProtector : IKeyProtector
{
    public static DpapiKeyProtector Instance { get; } = new();

    public byte[] Protect(byte[] key) => Run(key, protect: true);

    public byte[] Unprotect(byte[] wrapped) => Run(wrapped, protect: false);

    private static unsafe byte[] Run(byte[] data, bool protect)
    {
        fixed (byte* p = data)
        {
            var input = new DataBlob { Size = data.Length, Data = (nint)p };
            DataBlob output;
            bool ok = protect
                ? CryptProtectData(ref input, null, 0, 0, 0, UiForbidden, out output)
                : CryptUnprotectData(ref input, 0, 0, 0, 0, UiForbidden, out output);
            if (!ok)
                throw new CryptographicException("DPAPI failed", new Win32Exception(Marshal.GetLastPInvokeError()));
            try
            {
                return new ReadOnlySpan<byte>((void*)output.Data, output.Size).ToArray();
            }
            finally
            {
                new Span<byte>((void*)output.Data, output.Size).Clear();
                LocalFree(output.Data);
            }
        }
    }

    private const int UiForbidden = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public nint Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, nint entropy, nint reserved, nint prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
