# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors
# Builds the throwaway things Port manager, Cleaner and Uninstaller act on, inside the Sandbox only: listeners, a service, filled Windows
# folders and fake installed apps with leftovers. Runs elevated (the guest's logon), before the medium-integrity probe.
$ErrorActionPreference='Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or !(Test-Path 'C:\chippi\Chippi.Elevated.exe')) { throw 'Windows Sandbox only.' }
$bin='C:\fixture-bin'
New-Item -ItemType Directory $bin -Force | Out-Null
icacls.exe $bin /grant '*S-1-5-32-545:(OI)(CI)RX' | Out-Null

# ---- fixture programs (.NET Framework, compiled here) ----
Add-Type -OutputType WindowsApplication -OutputAssembly "$bin\fixture-window.exe" -ReferencedAssemblies System.Windows.Forms -TypeDefinition @'
using System; using System.Net; using System.Net.Sockets; using System.Windows.Forms;
public static class P {
    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, int revision, out IntPtr sd, IntPtr size);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)] static extern bool SetKernelObjectSecurity(IntPtr handle, int info, IntPtr sd);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    // Users may read this process (its name and start time), never end it: a holder only the helper can free.
    static void Share() {
        IntPtr sd; string me = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor("D:(A;;GA;;;SY)(A;;GA;;;" + me + ")(A;;0x1000;;;BU)", 1, out sd, IntPtr.Zero)) throw new Exception("sddl");
        if (!SetKernelObjectSecurity(GetCurrentProcess(), 4, sd)) throw new Exception("acl"); }
    [STAThread] public static void Main(string[] a) { Share();
    var l = new TcpListener(IPAddress.Loopback, int.Parse(a[0])); l.Start();
    var f = new Form { Text = "Fixture listener", Width = 320, Height = 120 };
    Application.Run(f); l.Stop(); } }
'@
Add-Type -OutputType WindowsApplication -OutputAssembly "$bin\fixture-hidden.exe" -TypeDefinition @'
using System; using System.Net; using System.Net.Sockets; using System.Threading;
public static class P {
    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, int revision, out IntPtr sd, IntPtr size);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)] static extern bool SetKernelObjectSecurity(IntPtr handle, int info, IntPtr sd);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    // Users may read this process (its name and start time), never end it: a holder only the helper can free.
    static void Share() {
        IntPtr sd; string me = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor("D:(A;;GA;;;SY)(A;;GA;;;" + me + ")(A;;0x1000;;;BU)", 1, out sd, IntPtr.Zero)) throw new Exception("sddl");
        if (!SetKernelObjectSecurity(GetCurrentProcess(), 4, sd)) throw new Exception("acl"); }
    public static void Main(string[] a) { Share();
    var l = new TcpListener(IPAddress.Loopback, int.Parse(a[0])); l.Start(); Thread.Sleep(Timeout.Infinite); } }
'@
Add-Type -OutputType ConsoleApplication -OutputAssembly "$bin\fixture-service.exe" -ReferencedAssemblies System.ServiceProcess -TypeDefinition @'
using System; using System.Net; using System.Net.Sockets; using System.ServiceProcess;
public sealed class S : ServiceBase {
    TcpListener l;
    public S() { ServiceName = "ChippiFixtureSvc"; CanStop = true; }
    protected override void OnStart(string[] a) { l = new TcpListener(IPAddress.Loopback, 47013); l.Start(); }
    protected override void OnStop() { l.Stop(); }
    public static void Main() { ServiceBase.Run(new S()); } }
'@
Add-Type -OutputType WindowsApplication -OutputAssembly "$bin\fixture-holder.exe" -TypeDefinition @'
using System; using System.IO; using System.Threading;
public static class P { public static void Main(string[] a) {
    using (var f = new FileStream(a[0], FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { Thread.Sleep(TimeSpan.FromMinutes(25)); } } }
'@
# The stand-in uninstaller: removes the app's program files and its Uninstall entry, leaving the leftovers a real one would. With "relaunch"
# it copies itself to Temp, starts the copy and exits at once, as many real uninstallers do; the copy finishes the work 3 s later.
# It notes the environment it got, so the run can report what an uninstaller sees from the helper.
Add-Type -OutputType WindowsApplication -OutputAssembly "$bin\fixture-uninstall.exe" -TypeDefinition @'
using System; using System.Diagnostics; using System.IO; using Microsoft.Win32;
public static class P { public static int Main(string[] a) {
    string hive = a[0], key = a[1], dir = a[2], mode = a.Length > 3 ? a[3] : "";
    string note = @"C:\results\uninstall-" + key + (mode == "child" ? "-child" : "") + ".txt";
    File.WriteAllText(note, "TEMP=" + Environment.GetEnvironmentVariable("TEMP") + "\r\nAPPDATA=" + (Environment.GetEnvironmentVariable("APPDATA") ?? "(none)")
        + "\r\nUSERPROFILE=" + (Environment.GetEnvironmentVariable("USERPROFILE") ?? "(none)") + "\r\nPATH=" + Environment.GetEnvironmentVariable("PATH")
        + "\r\nexe=" + Process.GetCurrentProcess().MainModule.FileName + "\r\n");
    if (mode == "relaunch") {
        string copy = Path.Combine(Path.GetTempPath(), "fixture-un-" + key + ".exe");
        File.Copy(Process.GetCurrentProcess().MainModule.FileName, copy, true);
        Process.Start(new ProcessStartInfo(copy, "\"" + hive + "\" \"" + key + "\" \"" + dir + "\" child") { UseShellExecute = false });
        return 0;
    }
    if (mode == "child") System.Threading.Thread.Sleep(3000);
    foreach (string f in new[] { "app.exe", "app.dll" }) { string p = Path.Combine(dir, f); if (File.Exists(p)) File.Delete(p); }
    var root = hive == "HKCU" ? RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64) : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
    root.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + key, false);
    return 0; } }
'@

# ---- Port manager ----
# The listeners run as a throwaway second user, so the probe's standard token can read them but not end them: only the helper can.
$chars=[char[]]'abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'
$secret=(-join (1..24 | ForEach-Object { $chars[(Get-Random -Maximum $chars.Length)] })) + '!7'
$password=ConvertTo-SecureString $secret -AsPlainText -Force
New-LocalUser -Name ChippiHolder -Password $password -AccountNeverExpires | Out-Null
$holderUser=New-Object System.Management.Automation.PSCredential("$env:COMPUTERNAME\ChippiHolder", $password)
$window=Start-Process "$bin\fixture-window.exe" -ArgumentList 47011 -Credential $holderUser -WorkingDirectory $bin -PassThru
$hidden=Start-Process "$bin\fixture-hidden.exe" -ArgumentList 47012 -Credential $holderUser -WorkingDirectory $bin -PassThru
sc.exe create ChippiFixtureSvc binPath= "$bin\fixture-service.exe" start= demand | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Fixture service create failed.' }
sc.exe start ChippiFixtureSvc | Out-Null
$deadline=(Get-Date).AddSeconds(15)
while (!((Get-NetTCPConnection -LocalPort 47011,47012,47013 -State Listen -ErrorAction SilentlyContinue | Measure-Object).Count -ge 3)) {
    if ((Get-Date) -gt $deadline) { throw 'Fixture listeners did not start.' }
    Start-Sleep -Milliseconds 200
}

# ---- Cleaner ----
$old=(Get-Date).AddDays(-3)
function New-Fixture([string]$path, [int]$kb, [bool]$aged=$true) {
    New-Item -ItemType Directory (Split-Path $path) -Force | Out-Null
    $bytes=New-Object byte[] ($kb*1024); (New-Object Random 7).NextBytes($bytes)
    [IO.File]::WriteAllBytes($path, $bytes)
    if ($aged) { $i=Get-Item -LiteralPath $path; $i.CreationTime=$old; $i.LastWriteTime=$old }
}
$win=$env:windir
$cleaner=[ordered]@{
    windowsTempOld="$win\Temp\ChippiFixture\old1.bin"; windowsTempNested="$win\Temp\ChippiFixture\sub\old2.bin"; windowsTempNew="$win\Temp\ChippiFixture\new.bin"
    windowsTempInUse="$win\Temp\ChippiFixture\inuse.bin"; windowsTempLink="$win\Temp\ChippiFixture\link"; keep='C:\ChippiKeep\keep.bin'
    updateOld="$win\SoftwareDistribution\Download\ChippiFixture\old.bin"
    deliveryOld="$win\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache\ChippiFixture\old.bin"
    dumpOld="$win\Minidump\fixture.dmp"; userTempOld="$env:TEMP\ChippiFixture\old.bin"; userTempNew="$env:TEMP\ChippiFixture\new.bin"
}
New-Fixture $cleaner.windowsTempOld 2048; New-Fixture $cleaner.windowsTempNested 1024; New-Fixture $cleaner.windowsTempNew 512 $false
New-Fixture $cleaner.windowsTempInUse 256; New-Fixture $cleaner.keep 64
New-Item -ItemType Junction -Path $cleaner.windowsTempLink -Target 'C:\ChippiKeep' | Out-Null
New-Fixture $cleaner.updateOld 3072; New-Fixture $cleaner.deliveryOld 1024; New-Fixture $cleaner.dumpOld 512
New-Fixture $cleaner.userTempOld 1024; New-Fixture $cleaner.userTempNew 128 $false
$holder=Start-Process "$bin\fixture-holder.exe" -ArgumentList "`"$($cleaner.windowsTempInUse)`"" -PassThru

# ---- Uninstaller ----
$uninstallRoot='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
function New-App([string]$key, [string]$name, [string]$publisher, [string]$dir, [string]$mode='', [string]$hive='HKLM') {
    New-Item -ItemType Directory $dir -Force | Out-Null
    Copy-Item "$bin\fixture-uninstall.exe" "$dir\uninstall.exe"
    'app' | Set-Content "$dir\app.exe"; 'dll' | Set-Content "$dir\app.dll"; 'readme' | Set-Content "$dir\readme.txt"
    $root=if ($hive -eq 'HKCU') { 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' } else { $uninstallRoot }
    $k=New-Item -Path "$root\$key" -Force
    $command="`"$dir\uninstall.exe`" $hive `"$key`" `"$dir`" $mode".Trim()
    foreach ($p in @{DisplayName=$name;Publisher=$publisher;DisplayVersion='1.0';InstallLocation=$dir;UninstallString=$command;QuietUninstallString=$command}.GetEnumerator()) {
        New-ItemProperty -Path $k.PSPath -Name $p.Key -Value $p.Value -PropertyType String -Force | Out-Null
    }
    New-ItemProperty -Path $k.PSPath -Name InstallDate -Value (Get-Date -Format yyyyMMdd) -PropertyType String -Force | Out-Null
}
$pf=$env:ProgramFiles
New-App ChippiFixtureNotes 'Fixture Notes' 'Fixture Labs' "$pf\Fixture Labs\Notes"
New-App ChippiFixtureRelaunch 'Fixture Relaunch' 'Fixture Labs' "$pf\Fixture Labs\Relaunch" relaunch
New-App ChippiFixtureWritable 'Fixture Writable' 'Fixture Works' "$pf\Fixture Works\Writable"
New-App 'Mozilla Firefox 999.0 (x64 en-US)' 'Mozilla Firefox' 'Mozilla' "$pf\Mozilla Firefox"
New-App 'Mozilla Thunderbird 999.0 (x64 en-US)' 'Mozilla Thunderbird' 'Mozilla' "$pf\Mozilla Thunderbird"
New-App ChippiFixtureUser 'Fixture User Tool' 'Fixture Labs' "$env:LOCALAPPDATA\Programs\Fixture User Tool" '' HKCU
# Write-data for Users on the uninstaller only (no delete): the helper's execution check must refuse it.
icacls.exe "$pf\Fixture Works\Writable\uninstall.exe" /grant '*S-1-5-32-545:(WD)' | Out-Null

# Leftovers. Notes: data in ProgramData and AppData, a key, a Start menu shortcut, and a junction in its install folder that must not be followed.
New-Fixture 'C:\ProgramData\Fixture Labs\Notes\settings.ini' 4 $false
New-Fixture "$env:APPDATA\Fixture Labs\Notes\user.ini" 4 $false
New-Item -Path 'HKLM:\SOFTWARE\Fixture Labs\Notes' -Force | Out-Null
New-ItemProperty -Path 'HKLM:\SOFTWARE\Fixture Labs\Notes' -Name Theme -Value dark -Force | Out-Null
# Named by the record (Software\<publisher>\<name>): the one machine key the helper removes.
New-Item -Path 'HKLM:\SOFTWARE\Fixture Labs\Fixture Notes' -Force | Out-Null
New-ItemProperty -Path 'HKLM:\SOFTWARE\Fixture Labs\Fixture Notes' -Name Window -Value 1 -Force | Out-Null
$menu="$env:ProgramData\Microsoft\Windows\Start Menu\Programs"
$shell=New-Object -ComObject WScript.Shell
$lnk=$shell.CreateShortcut("$menu\Fixture Notes.lnk"); $lnk.TargetPath="$pf\Fixture Labs\Notes\app.exe"; $lnk.Save()
New-Fixture 'C:\ChippiKeepNotes\keep.txt' 4 $false
New-Item -ItemType Junction -Path "$pf\Fixture Labs\Notes\cache" -Target 'C:\ChippiKeepNotes' | Out-Null
# Firefox with Thunderbird installed: the Mozilla folders are shared and never offered.
New-Fixture 'C:\ProgramData\Mozilla\shared.ini' 4 $false
New-Fixture "$env:APPDATA\Mozilla\Firefox\profiles.ini" 4 $false
New-Fixture "$env:APPDATA\Mozilla\Thunderbird\profiles.ini" 4 $false
New-Item -Path 'HKLM:\SOFTWARE\Mozilla\Mozilla Firefox' -Force | Out-Null
# The user app's leftover.
New-Fixture "$env:APPDATA\Fixture User Tool\state.json" 4 $false

@{ ports=@{ window=@{port=47011;pid=$window.Id}; hidden=@{port=47012;pid=$hidden.Id}; service=@{port=47013;name='ChippiFixtureSvc'} }
   cleaner=$cleaner; holderPid=$holder.Id; guestLabel=((whoami /groups | Select-String 'Mandatory Label') -join ' ') } | ConvertTo-Json -Depth 5 | Set-Content C:\results\fixture.json
