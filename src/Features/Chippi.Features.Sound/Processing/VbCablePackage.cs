// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Chippi.Audio;

namespace Chippi.Features.Sound.Processing;

/// <summary>build/vendor/vb-cable.json: what the shipped VB-CABLE package must be, byte for byte.</summary>
public sealed record VbCablePin(string Url, string File, long Bytes, string Sha256, string Setup, string SetupSha256, string SignerThumbprint, string SignerName)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static VbCablePin Load(string path) =>
        JsonSerializer.Deserialize<VbCablePin>(System.IO.File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty VB-CABLE pin");
}

/// <summary>The package is missing, altered, or its setup isn't signed by the pinned VB-Audio signer.</summary>
public sealed class DriverPackageException(string message) : AudioFailure(message);

/// <summary>
/// VB-CABLE as Chippi ships it: the unmodified zip and its pin in <c>vendor\vb-cable</c> next to
/// Chippi.exe (architecture §6). <see cref="Prepare"/> re-checks everything before setup may run.
/// </summary>
public static class VbCablePackage
{
    public static string VendorFolder { get; } = Path.Combine(AppContext.BaseDirectory, "vendor", "vb-cable");

    /// <summary>
    /// Checks the zip against the pin, extracts it whole and fresh (the setup reads its INF and drivers
    /// from its own folder) to <paramref name="extractRoot"/>\&lt;zip hash&gt;, then checks the setup exe's hash
    /// and that its Authenticode signature is valid and from the pinned certificate. Returns the
    /// setup's path. Never runs anything.
    /// </summary>
    public static string Prepare(string vendorFolder, string extractRoot)
    {
        var pin = VbCablePin.Load(Path.Combine(vendorFolder, "vb-cable.json"));
        var zip = Path.Combine(vendorFolder, pin.File);
        if (!File.Exists(zip))
            throw new DriverPackageException("Driver package missing");
        if (new FileInfo(zip).Length != pin.Bytes || !HashIs(zip, pin.Sha256))
            throw new DriverPackageException("Driver package damaged");

        // Extracted fresh every time: setup reads the INF, SYS and CAT beside it, so an earlier
        // extraction is never trusted, only the zip just checked.
        var folder = Path.Combine(extractRoot, pin.Sha256);
        var setup = Path.Combine(folder, pin.Setup);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        ZipFile.ExtractToDirectory(zip, folder);
        if (!File.Exists(setup) || !HashIs(setup, pin.SetupSha256) || !SignedBy(setup, pin.SignerThumbprint))
            throw new DriverPackageException("Driver package damaged");
        return setup;
    }

    private static bool HashIs(string path, string sha256)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A valid Authenticode signature (timestamped, so an expired certificate still counts) from the pinned certificate.</summary>
    internal static bool SignedBy(string path, string thumbprint)
    {
        if (WinTrust.Verify(path) != 0)
            return false;
#pragma warning disable SYSLIB0057 // the signer of a signed file; X509CertificateLoader has no Authenticode reader
        using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
        return signer.Thumbprint.Equals(thumbprint, StringComparison.OrdinalIgnoreCase);
    }

    private static class WinTrust
    {
        private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        private const uint UiNone = 2;
        private const uint RevokeNone = 0;
        private const uint ChoiceFile = 1;
        private const uint CacheOnlyUrlRetrieval = 0x1000; // no network: Chippi makes no calls out

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileInfo
        {
            public uint Size;
            public string Path;
            public IntPtr Handle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Data
        {
            public uint Size;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr File;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;
        }

#pragma warning disable SYSLIB1054 // struct marshalling through DllImport keeps the project free of unsafe code
        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr window, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref Data data);
#pragma warning restore SYSLIB1054

        public static int Verify(string path)
        {
            var file = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = path };
            var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
            try
            {
                Marshal.StructureToPtr(file, filePtr, false);
                var data = new Data
                {
                    Size = (uint)Marshal.SizeOf<Data>(),
                    UiChoice = UiNone,
                    RevocationChecks = RevokeNone,
                    UnionChoice = ChoiceFile,
                    File = filePtr,
                    ProviderFlags = CacheOnlyUrlRetrieval,
                };
                return WinVerifyTrust(new IntPtr(-1), GenericVerifyV2, ref data);
            }
            finally
            {
                Marshal.DestroyStructure<FileInfo>(filePtr);
                Marshal.FreeHGlobal(filePtr);
            }
        }
    }
}

/// <summary>Runs VB-Audio's setup. Tests replace it; nothing in a test may start the real one.</summary>
public interface IDriverSetup
{
    /// <summary>The setup's exit code, or null when the admin prompt was cancelled.</summary>
    Task<int?> RunAsync(string setupPath, CancellationToken ct);
}

/// <summary>Starts the setup elevated (one admin prompt); Chippi itself stays a standard-user process.</summary>
public sealed class ElevatedDriverSetup : IDriverSetup
{
    private const int Cancelled = 1223; // ERROR_CANCELLED: the user declined the admin prompt

    public async Task<int?> RunAsync(string setupPath, CancellationToken ct)
    {
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(setupPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(setupPath)!,
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Cancelled)
        {
            return null;
        }
        if (process is null)
            return null;
        using (process)
        {
            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
    }
}
