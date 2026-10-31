// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Chippi.Audio;

public sealed record AppDeviceChoice(bool Available, string? EndpointId = null, bool HasSession = true);

/// <summary>A policy probe/write failed, distinct from a stale app or endpoint selection.</summary>
public sealed class AppOutputPolicyException() : AudioFailure("App output unavailable");

/// <summary>Optional per-app policy capability. Calls stay on the audio engine thread.</summary>
public interface IPerAppDeviceSystem
{
    AppDeviceChoice GetAppOutput(int processId);
    bool SetAppOutput(int processId, string? endpointId);
}

/// <summary>
/// Undocumented Windows policy, verified by the Phase 3 spike on build 26300 x64 only. No activation
/// or vtable call is made outside that gate: a Windows that reorders the interface under the same
/// IID can't be told apart at runtime (docs/phase-3/architecture.md). A format Chippi doesn't know
/// turns this instance off at once; other failures may be one app's (a process that just closed), so
/// it takes <see cref="FailuresAllowed"/> in a row. Volume and mute continue through documented Core
/// Audio. Never calls the clear-all slot.
/// </summary>
internal sealed class PerAppDevicePolicy(ILogger logger) : IDisposable
{
    private const string Prefix = @"\\?\SWD#MMDEVAPI#";
    private const string Suffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    internal const int FailuresAllowed = 3;
    private IntPtr _factory;
    private bool _disabled;
    private bool _initialized;
    private int _failures;

    public static bool IsVerifiedBuild(Version version, Architecture architecture) =>
        version.Major == 10 && version.Build == 26300 && architecture == Architecture.X64;

    /// <summary>Why this PC has no per-app output, for Settings; null where it can have it.</summary>
    public static string? Unsupported(Version version, Architecture os, Architecture process) =>
        os != Architecture.X64 || process != Architecture.X64 ? "Not available on Arm PCs"
        : version.Major != 10 || version.Build < 26300 ? "Needs Windows build 26300"
        : !IsVerifiedBuild(version, os) ? $"Not checked on Windows build {version.Build} yet"
        : null;

    public AppDeviceChoice Get(int pid)
    {
        if (!Ensure()) return new(false);
        try
        {
            var endpoint = Read(pid, AudioRole.Multimedia);
            _failures = 0;
            return new(true, endpoint);
        }
        catch (Exception ex) { Fail(ex, "read"); return new(false); }
    }

    public bool Set(int pid, string? endpoint)
    {
        if (!Ensure()) return false;
        try
        {
            // Probe every role before any write, and verify each write by reading it back.
            foreach (var role in Enum.GetValues<AudioRole>()) _ = Read(pid, role);
            var set = Marshal.GetDelegateForFunctionPointer<SetEndpoint>(Slot(25));
            foreach (var role in Enum.GetValues<AudioRole>())
            {
                var text = endpoint is null ? IntPtr.Zero : HString(Prefix + endpoint + Suffix);
                try { Marshal.ThrowExceptionForHR(set(_factory, (uint)pid, 0, (int)role, text)); }
                finally { if (text != IntPtr.Zero) WindowsDeleteString(text); }
                if (!string.Equals(Read(pid, role), endpoint, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("App output readback differs");
            }
            _failures = 0;
            return true;
        }
        catch (Exception ex) { Fail(ex, "write"); return false; }
    }

    private void Fail(Exception ex, string call)
    {
        _disabled = ex is FormatException || ++_failures >= FailuresAllowed;
        logger.LogWarning(ex, "Per-app output {Call} failed on Windows {Build}{Off}", call, Environment.OSVersion.Version, _disabled ? "; turned off" : "");
    }

    private bool Ensure()
    {
        if (_disabled) return false;
        if (_factory != IntPtr.Zero) return true;
        if (Unsupported(Environment.OSVersion.Version, RuntimeInformation.OSArchitecture, RuntimeInformation.ProcessArchitecture) is not null)
        { _disabled = true; return false; }
        try
        {
            if (!_initialized)
            {
                Marshal.ThrowExceptionForHR(RoInitialize(1));
                _initialized = true;
            }
            var name = HString("Windows.Media.Internal.AudioPolicyConfig");
            try
            {
                var iid = new Guid("ab3d4648-e242-459f-b02f-541c70306324");
                Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref iid, out _factory));
            }
            finally { WindowsDeleteString(name); }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Per-app output unavailable on Windows {Build}", Environment.OSVersion.Version); }
        _disabled = _factory == IntPtr.Zero;
        return !_disabled;
    }

    private string? Read(int pid, AudioRole role)
    {
        var get = Marshal.GetDelegateForFunctionPointer<GetEndpoint>(Slot(26));
        Marshal.ThrowExceptionForHR(get(_factory, (uint)pid, 0, (int)role, out var value));
        try
        {
            var ptr = WindowsGetStringRawBuffer(value, out var length);
            var text = Marshal.PtrToStringUni(ptr, (int)length) ?? "";
            if (text.Length == 0) return null;
            if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !text.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Unknown app output format");
            return text[Prefix.Length..^Suffix.Length];
        }
        finally { if (value != IntPtr.Zero) WindowsDeleteString(value); }
    }

    private IntPtr Slot(int index) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(_factory), index * IntPtr.Size);
    private static IntPtr HString(string text)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(text, (uint)text.Length, out var value));
        return value;
    }

    public void Dispose()
    {
        if (_factory != IntPtr.Zero) { Marshal.Release(_factory); _factory = IntPtr.Zero; }
        if (_initialized) { RoUninitialize(); _initialized = false; }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetEndpoint(IntPtr self, uint pid, int flow, int role, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetEndpoint(IntPtr self, uint pid, int flow, int role, IntPtr value);
#pragma warning disable SYSLIB1054 // WinRT HSTRING and delegates; no unsafe project code
    [DllImport("combase.dll")] private static extern int RoInitialize(uint type);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IntPtr factory);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, uint length, out IntPtr value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")] private static extern IntPtr WindowsGetStringRawBuffer(IntPtr value, out uint length);
#pragma warning restore SYSLIB1054
}