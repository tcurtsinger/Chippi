// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Shortcuts;

/// <summary>
/// Windows' "Use the Print screen key to open screen capture" (<c>HKCU\Control Panel\Keyboard</c>,
/// <c>PrintScreenKeyForSnippingEnabled</c>), and what Chippi found there before it first turned it off
/// (docs/phase-7/capture-layer.md, Print Screen). Only Chippi.exe in the real profile changes the real one; dev profiles
/// and tests get <see cref="MemoryPrintScreenSetting"/>.
/// </summary>
public interface IPrintScreenSetting
{
    /// <summary>The value: null while unset (Windows treats that as on), 0 off, 1 on.</summary>
    int? Read();

    /// <summary>Writes the value (null deletes it) and tells Windows the setting changed.</summary>
    void Write(int? value);

    /// <summary>What the value was before Chippi first wrote 0, kept in Chippi's own settings; null while Chippi doesn't hold the key.</summary>
    PrintScreenClaim? Claim { get; set; }

    /// <summary>The value changed outside Chippi (Travis turned the switch back on). Raised on the UI thread.</summary>
    event Action? Changed;
}

/// <summary>Chippi holds Print Screen; <paramref name="Found"/> is what Windows' value was before (null: unset).</summary>
public sealed record PrintScreenClaim(int? Found);

/// <summary>A setting held in memory, for dev profiles and tests: the real one is never touched.</summary>
public sealed class MemoryPrintScreenSetting(int? value = null) : IPrintScreenSetting
{
    public int? Value { get; private set; } = value;

    public int Writes { get; private set; }

    public PrintScreenClaim? Claim { get; set; }

    public event Action? Changed;

    public int? Read() => Value;

    public void Write(int? value)
    {
        Value = value;
        Writes++;
    }

    /// <summary>Travis flipping Windows' switch himself.</summary>
    public void SetOutside(int? value)
    {
        Value = value;
        Changed?.Invoke();
    }
}
