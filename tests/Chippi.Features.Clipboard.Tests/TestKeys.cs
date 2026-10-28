// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Clipboard.Storage;

namespace Chippi.Features.Clipboard.Tests;

/// <summary>Stands in for DPAPI.</summary>
internal sealed class TestKeys : IKeyProtector
{
    public byte[] Protect(byte[] key) => [.. key.Select(b => (byte)(b ^ 0x5A))];

    public byte[] Unprotect(byte[] wrapped) => [.. wrapped.Select(b => (byte)(b ^ 0x5A))];
}

/// <summary>A folder of its own per test, deleted afterwards.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chippi-clipboard-feature-tests", Guid.NewGuid().ToString("N"));

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
