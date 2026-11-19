// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Chippi.Elevated;

/// <summary>The real Windows behind <see cref="IUninstallHost"/>.</summary>
public sealed class NativeUninstallHost : IUninstallHost
{
    private static readonly IReadOnlyDictionary<string, string> Variables = SystemEnvironment.FixedVariables();
    /// <summary>Only Windows' own folders (<see cref="SystemEnvironment"/>), never the helper's inherited environment.</summary>
    public string Expand(string text) => SystemEnvironment.Expand(text, Variables);
    public bool FileExists(string path) => File.Exists(path);
    public string SystemDirectory => Environment.SystemDirectory;
}

/// <summary>Registry plumbing both native registry classes share. Nothing here is reached by a test: tests use fakes and never touch the real registry.</summary>
internal static unsafe class NativeRegistry
{
    public static readonly IntPtr LocalMachine = new(unchecked((int)0x80000002));
    public const uint KeyRead = 0x20019, KeyWrite = 0x20006, Wow64Key = 0x100, Wow32Key = 0x200, OptionOpenLink = 8;
    private const int ErrorSuccess = 0;

    /// <summary>Opens the key itself, without following it when it is a link; link says it is one (a link key holds a SymbolicLinkValue).</summary>
    public static IntPtr Open(string path, uint access, out bool link)
    {
        link = false;
        if (RegOpenKeyExW(LocalMachine, path, OptionOpenLink, access, out var handle) != ErrorSuccess) return IntPtr.Zero;
        link = IsLink(handle);
        return handle;
    }

    public static bool IsLink(IntPtr handle)
    {
        uint type = 0, length = 0;
        return RegQueryValueExW(handle, "SymbolicLinkValue", IntPtr.Zero, ref type, IntPtr.Zero, ref length) == ErrorSuccess;
    }

    public static void Close(IntPtr handle) { if (handle != IntPtr.Zero) RegCloseKey(handle); }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegOpenKeyExW(IntPtr key, string subKey, uint options, uint access, out IntPtr result);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegCreateKeyExW(IntPtr key, string subKey, uint reserved, IntPtr cls, uint options, uint access, SecurityAttributes* attributes,
        out IntPtr result, out uint disposition);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegQueryValueExW(IntPtr key, string name, IntPtr reserved, ref uint type, IntPtr data, ref uint length);
    [DllImport("advapi32.dll")] public static extern int RegCloseKey(IntPtr key);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("advapi32.dll")]
    public static extern uint GetSecurityInfo(IntPtr handle, int type, uint information, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl,
        out IntPtr descriptor);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AccessCheck(IntPtr descriptor, SafeAccessTokenHandle token, uint desired, ref GenericMapping mapping, IntPtr privileges,
        ref uint length, out uint granted, [MarshalAs(UnmanagedType.Bool)] out bool allowed);
    [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)] public struct SecurityAttributes { public uint Length; public IntPtr Descriptor; public int Inherit; }
    [StructLayout(LayoutKind.Sequential)] public struct GenericMapping { public uint Read, Write, Execute, All; }

    private const uint KeySetValue = 0x2, KeyCreateSubKey = 0x4, KeyCreateLink = 0x20, DeleteRight = 0x10000, WriteDac = 0x40000, WriteOwner = 0x80000;
    public const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    /// <summary>
    /// The key is owned by Administrators or SYSTEM (or TrustedInstaller when allowed), and the standard token holds no right to set a value,
    /// create a subkey or link, delete it or change its DACL or owner. A key anyone standard can write is not believed.
    /// </summary>
    public static bool TrustedKey(IntPtr handle, SafeAccessTokenHandle token, bool allowTrustedInstaller)
    {
        if (GetSecurityInfo(handle, 4, 7, out var owner, out _, out _, out _, out var descriptor) != 0) return false;
        try
        {
            string sid = new SecurityIdentifier(owner).Value;
            if (sid is not ("S-1-5-32-544" or "S-1-5-18") && !(allowTrustedInstaller && sid == TrustedInstaller)) return false;
            var mapping = new GenericMapping { Read = 0x20019, Write = 0x20006, Execute = 0x20019, All = 0xF003F };
            return (Granted(descriptor, token, mapping) & (KeySetValue | KeyCreateSubKey | KeyCreateLink | DeleteRight | WriteDac | WriteOwner)) == 0;
        }
        finally { LocalFree(descriptor); }
    }

    /// <summary>What AccessCheck grants the standard token on a security descriptor (MAXIMUM_ALLOWED).</summary>
    public static uint Granted(IntPtr descriptor, SafeAccessTokenHandle token, GenericMapping mapping)
    {
        uint size = 4096;
        var privileges = Marshal.AllocHGlobal((int)size);
        try
        {
            if (!AccessCheck(descriptor, token, 0x02000000, ref mapping, privileges, ref size, out uint granted, out _)) throw new Win32Exception();
            return granted;
        }
        finally { Marshal.FreeHGlobal(privileges); }
    }
}

/// <summary>Reads HKLM Uninstall keys in either registry view, never following a registry link. A key the standard token could change is read as untrusted: its values are not returned.</summary>
public sealed class NativeUninstallRegistry(SafeAccessTokenHandle standardToken) : IUninstallRegistry
{
    private const string Base = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";

    private static uint Access(int view) => NativeRegistry.KeyRead | (view == 64 ? NativeRegistry.Wow64Key : NativeRegistry.Wow32Key);

    public UninstallKey? Read(int view, string keyName)
    {
        var handle = NativeRegistry.Open(Base + keyName, Access(view), out bool link);
        if (handle == IntPtr.Zero) return null;
        if (link)
        {
            NativeRegistry.Close(handle);
            return new UninstallKey(true, null, null, null, null, null, null, null, null);
        }
        if (!NativeRegistry.TrustedKey(handle, standardToken, allowTrustedInstaller: true))
        {
            NativeRegistry.Close(handle);
            return new UninstallKey(false, null, null, null, null, null, null, null, null, Untrusted: true);
        }
        using var key = RegistryKey.FromHandle(new SafeRegistryHandle(handle, true), view == 64 ? RegistryView.Registry64 : RegistryView.Registry32);
        return new UninstallKey(false, Int(key, "SystemComponent"), Str(key, "ParentKeyName"), Int(key, "WindowsInstaller"), Str(key, "DisplayName"),
            Str(key, "Publisher"), Str(key, "InstallLocation"), Str(key, "QuietUninstallString"), Str(key, "UninstallString"));
    }

    public bool Exists(int view, string keyName)
    {
        var handle = NativeRegistry.Open(Base + keyName, Access(view), out _);
        NativeRegistry.Close(handle);
        return handle != IntPtr.Zero;
    }

    private static int? Int(RegistryKey key, string name) => key.GetValue(name) is int value ? value : null;
    private static string? Str(RegistryKey key, string name) => key.GetValue(name) is string value ? value : null;
}

/// <summary>
/// The records under HKLM\Software\Chippi\Elevated\UninstallRecords (64-bit view). Each key on the way is created with a protected DACL
/// (Administrators and SYSTEM, inheritable) and refused when it is a link or when the standard token is granted any right to change it.
/// </summary>
public sealed unsafe class NativeRecordStore(SafeAccessTokenHandle standardToken) : IRecordStore, IDisposable
{
    private static readonly string[] Keys = [@"SOFTWARE\Chippi", @"SOFTWARE\Chippi\Elevated", @"SOFTWARE\Chippi\Elevated\UninstallRecords"];
    private const string ProtectedDacl = "D:P(A;CI;KA;;;BA)(A;CI;KA;;;SY)";
    private RegistryKey? _records;

    public bool Prepare()
    {
        _records?.Dispose();
        _records = null;
        if (!NativeRegistry.ConvertStringSecurityDescriptorToSecurityDescriptorW(ProtectedDacl, 1, out var descriptor, out _)) return false;
        try
        {
            var attributes = new NativeRegistry.SecurityAttributes { Length = (uint)sizeof(NativeRegistry.SecurityAttributes), Descriptor = descriptor };
            for (int i = 0; i < Keys.Length; i++)
            {
                if (NativeRegistry.RegCreateKeyExW(NativeRegistry.LocalMachine, Keys[i], 0, IntPtr.Zero, NativeRegistry.OptionOpenLink,
                    NativeRegistry.KeyRead | NativeRegistry.KeyWrite | NativeRegistry.Wow64Key, &attributes, out var handle, out _) != 0) return false;
                bool good = !NativeRegistry.IsLink(handle) && Trusted(handle);
                if (!good) { NativeRegistry.Close(handle); return false; }
                if (i == Keys.Length - 1) _records = RegistryKey.FromHandle(new SafeRegistryHandle(handle, true), RegistryView.Registry64);
                else NativeRegistry.Close(handle);
            }
            return _records is not null;
        }
        finally { NativeRegistry.LocalFree(descriptor); }
    }

    private bool Trusted(IntPtr handle) => NativeRegistry.TrustedKey(handle, standardToken, allowTrustedInstaller: false);

    private RegistryKey Records => _records ?? throw new InvalidOperationException("Records not prepared.");

    public IReadOnlyList<UninstallRecord> List()
    {
        var all = new List<UninstallRecord>();
        foreach (string name in Records.GetSubKeyNames())
        {
            if (!Guid.TryParseExact(name, "N", out var id)) continue;
            all.Add(Get(id) ?? new UninstallRecord(id, 0, "", null, null, null, null, null, DateTime.MinValue));
        }
        return all;
    }

    public UninstallRecord? Get(Guid id)
    {
        try
        {
            using var key = Records.OpenSubKey(id.ToString("N"));
            if (key is null) return null;
            if (key.GetValue("Created") is not long ticks || key.GetValue("View") is not int view || key.GetValue("KeyName") is not string keyName) return null;
            return new UninstallRecord(id, view, keyName, Str(key, "DisplayName"), Str(key, "Publisher"), Str(key, "InstallLocation"), Str(key, "ProductCode"),
                Str(key, "UninstallerFolder"), new DateTime(ticks, DateTimeKind.Utc));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { return null; }
    }

    public void Write(UninstallRecord record)
    {
        using var key = Records.CreateSubKey(record.Id.ToString("N"));
        key.SetValue("View", record.View, RegistryValueKind.DWord);
        key.SetValue("KeyName", record.KeyName, RegistryValueKind.String);
        Set(key, "DisplayName", record.DisplayName); Set(key, "Publisher", record.Publisher); Set(key, "InstallLocation", record.InstallLocation);
        Set(key, "ProductCode", record.ProductCode); Set(key, "UninstallerFolder", record.UninstallerFolder);
        key.SetValue("Created", record.CreatedUtc.Ticks, RegistryValueKind.QWord);
    }

    public void Delete(Guid id) => Records.DeleteSubKeyTree(id.ToString("N"), throwOnMissingSubKey: false);

    private static void Set(RegistryKey key, string name, string? value) { if (value is not null) key.SetValue(name, value, RegistryValueKind.String); }
    private static string? Str(RegistryKey key, string name) => key.GetValue(name) is string value ? value : null;
    public void Dispose() { _records?.Dispose(); _records = null; }
}

/// <summary>
/// Opens files and folders for the execution check: no link followed, the file shared for read only and a folder without FILE_SHARE_DELETE, so
/// nothing can be rewritten, renamed or swapped while the handle is held. AccessCheck answers for the standard token.
/// </summary>
public sealed class NativeFsProbe(SafeAccessTokenHandle standardToken) : IFsProbe
{
    public IHeldNode? Open(string path, bool file)
    {
        // FILE_READ_DATA (FILE_LIST_DIRECTORY) is needed beside READ_CONTROL and FILE_READ_ATTRIBUTES: a handle that asks only for attribute-type rights is skipped by
        // the sharing check, so it would not stop a rename or delete.
        var handle = CreateFile(path, 0x20081, file ? 1u : 3u, 0, 3, 0x02200000, 0);
        if (handle.IsInvalid) { handle.Dispose(); return null; }
        try
        {
            if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception();
            var final = new StringBuilder(32768);
            uint length = GetFinalPathNameByHandle(handle, final, final.Capacity, 0);
            bool matches = length != 0 && length < final.Capacity && final.Length > 4
                && string.Equals(final.ToString()[4..].TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            uint error = GetSecurityInfo(handle, 1, 7, out var owner, out _, out _, out _, out var descriptor);
            if (error != 0) throw new Win32Exception((int)error);
            try
            {
                var mapping = new NativeRegistry.GenericMapping { Read = 0x120089, Write = 0x120116, Execute = 0x1200A0, All = 0x1F01FF };
                return new Held(handle, (info.Attributes & 0x400) != 0, matches, new SecurityIdentifier(owner).Value, NativeRegistry.Granted(descriptor, standardToken, mapping));
            }
            finally { NativeRegistry.LocalFree(descriptor); }
        }
        catch { handle.Dispose(); throw; }
    }

    private sealed class Held(SafeFileHandle handle, bool reparse, bool matches, string owner, uint granted) : IHeldNode
    {
        public bool IsReparsePoint => reparse;
        public bool PathMatches => matches;
        public string OwnerSid => owner;
        public uint StandardGranted => granted;
        public void Dispose() => handle.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string file, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation info);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, int count, uint flags);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityInfo(SafeFileHandle handle, int type, uint information,
        out nint owner, out nint group, out nint dacl, out nint sacl, out nint descriptor);
}

/// <summary>
/// CreateProcess with an explicit application name, created suspended, put in a job object (no limits, so the uninstaller outlives the helper if
/// it must) and then resumed. The job is the wait: it is empty when the uninstaller and everything it started have ended.
/// </summary>
public sealed class NativeProcessLauncher : IProcessLauncher
{
    private const uint CreateSuspended = 0x4, CreateUnicodeEnvironment = 0x400;

    public unsafe ILaunchedJob Start(string application, string commandLine, string workingDirectory)
    {
        var info = new StartupInfo { Size = (uint)sizeof(StartupInfo) };
        var environment = SystemEnvironmentBlock();
        var line = (commandLine + "\0").ToCharArray();
        IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception();
        try
        {
            ProcessInformation process;
            fixed (char* app = application) fixed (char* cl = line) fixed (char* dir = workingDirectory) fixed (char* env = environment)
                if (!CreateProcessW(app, cl, IntPtr.Zero, IntPtr.Zero, false, CreateSuspended | CreateUnicodeEnvironment, (IntPtr)env, dir, ref info, out process))
                    throw new Win32Exception();
            try
            {
                if (!AssignProcessToJobObject(job, process.Process)) throw new Win32Exception();
                if (ResumeThread(process.Thread) == uint.MaxValue) throw new Win32Exception();
            }
            catch { TerminateProcess(process.Process, 1); CloseHandle(process.Thread); CloseHandle(process.Process); throw; }
            CloseHandle(process.Thread);
            return new Job(job, process.Process);
        }
        catch { CloseHandle(job); throw; }
    }

    /// <summary>
    /// The uninstaller's environment: Windows' default system variables (CreateEnvironmentBlock with no token), scrubbed. It never inherits the helper's,
    /// which carries whatever the owner's profile set. When Windows can't give one, the start fails.
    /// </summary>
    private static unsafe char[] SystemEnvironmentBlock()
    {
        if (!CreateEnvironmentBlock(out var block, IntPtr.Zero, false)) throw new Win32Exception();
        var variables = new List<KeyValuePair<string, string>>();
        try
        {
            for (char* p = (char*)block; *p != '\0';)
            {
                var entry = new string(p);
                p += entry.Length + 1;
                int equals = entry.IndexOf('=', 1);   // a name may begin with "=" (drive directories)
                if (equals > 0 && entry[0] != '=') variables.Add(new(entry[..equals], entry[(equals + 1)..]));
            }
        }
        finally { DestroyEnvironmentBlock(block); }
        return SystemEnvironment.Block(SystemEnvironment.Scrub(variables, Environment.SystemDirectory));
    }

    [DllImport("userenv.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool inherit);
    [DllImport("userenv.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    private sealed class Job(IntPtr job, IntPtr process) : ILaunchedJob
    {
        private IntPtr _job = job, _process = process;

        public async Task<int> WaitAsync(CancellationToken ct)
        {
            int exit = 0;
            bool have = false;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!have && Exit(out exit)) have = true;
                if (Active() == 0)
                {
                    if (!have) Exit(out exit);
                    return exit;
                }
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
        }

        private bool Exit(out int exit)
        {
            exit = 0;
            if (!GetExitCodeProcess(_process, out uint code) || code == 259) return false;   // 259 is STILL_ACTIVE
            exit = unchecked((int)code);
            return true;
        }

        /// <summary>JOBOBJECT_BASIC_ACCOUNTING_INFORMATION: four 8-byte counters, then TotalPageFaultCount, TotalProcesses, ActiveProcesses.</summary>
        private unsafe uint Active()
        {
            byte* buffer = stackalloc byte[48];
            if (!QueryInformationJobObject(_job, 1, buffer, 48, out _)) throw new Win32Exception();
            return *(uint*)(buffer + 40);
        }

        public void Dispose()
        {
            CloseHandle(Interlocked.Exchange(ref _job, IntPtr.Zero));
            CloseHandle(Interlocked.Exchange(ref _process, IntPtr.Zero));
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo
    {
        public uint Size; public IntPtr Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, Reserved2Size; public IntPtr Reserved2, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool CreateProcessW(char* application, char* commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, char* directory, ref StartupInfo info, out ProcessInformation process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool QueryInformationJobObject(IntPtr job, int infoClass, byte* info, int length, out int returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
