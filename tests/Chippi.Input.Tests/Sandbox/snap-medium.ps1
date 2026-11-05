# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# Windows Sandbox's logon command runs elevated. Run Snap and its target apps as an ordinary user,
# preserving the production rule that elevated windows cannot be moved. This runs only inside Sandbox.
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or -not (Test-Path -LiteralPath 'C:\chippi\Chippi.exe')) {
    throw 'This launcher runs only inside the Windows Sandbox check'
}
$localRoot = Join-Path ([Environment]::GetFolderPath("CommonDocuments")) 'ChippiSnapSandbox'
$localApp = Join-Path $localRoot 'app'
$localProbe = Join-Path $localRoot 'probe'
$localResults = Join-Path $localRoot 'results'
New-Item -ItemType Directory -Force -Path $localRoot,$localResults | Out-Null
# The guest's inherited Public ACL can be noncanonical. This is a new test-only directory:
# give it a fresh, canonical ACL before copying any files.
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false)
foreach ($entry in @(@('S-1-5-32-545','Modify'),@('S-1-5-32-544','FullControl'),@('S-1-5-18','FullControl'),
    @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value,'FullControl'))) {
    $sid = New-Object Security.Principal.SecurityIdentifier $entry[0]
    $rule = New-Object Security.AccessControl.FileSystemAccessRule $sid, $entry[1], 'ContainerInherit,ObjectInherit', 'None', 'Allow'
    $acl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $localRoot -AclObject $acl
Copy-Item -LiteralPath C:\chippi -Destination $localApp -Recurse
Copy-Item -LiteralPath C:\probe -Destination $localProbe -Recurse
Copy-Item -LiteralPath C:\results\checks.txt -Destination (Join-Path $localResults 'checks.txt')
# Restricted processes cannot use the host's mapped folders on every Sandbox image. Local user files
# inherit the account SID's permissions; only the elevated wrapper copies results back to the mapping.
Get-ChildItem -LiteralPath $localProbe -Filter *.ps1 | ForEach-Object {
    $script = [IO.File]::ReadAllText($_.FullName).Replace('C:\results',$localResults).Replace('C:\chippi',$localApp).Replace('C:\probe',$localProbe)
    [IO.File]::WriteAllText($_.FullName,$script)
}
$env:CHIPPI_SNAP_LOCAL = $localRoot
Add-Type @"
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
public static class SnapMedium {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {
        public int Size; public string Reserved,Desktop,Title;
        public int X,Y,Width,Height,CharsX,CharsY,Fill,Flags; public short Show,ReservedCount;
        public IntPtr ReservedData,Input,Output,Error;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process,Thread; public int Pid,Tid; }
    [StructLayout(LayoutKind.Sequential)] struct Label { public IntPtr Sid; public uint Attributes; }
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr p,uint access,out IntPtr t);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool CreateRestrictedToken(IntPtr t,uint flags,uint disabled,IntPtr sids,uint privileges,IntPtr removed,uint restricted,IntPtr restrict,out IntPtr result);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ConvertStringSidToSid(string s,out IntPtr sid);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool SetTokenInformation(IntPtr t,int cls,ref Label info,int size);
    [DllImport("advapi32.dll")] static extern int GetLengthSid(IntPtr sid);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h,uint ms);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h,out uint code);
    [DllImport("advapi32.dll",EntryPoint="CreateProcessAsUserW",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool CreateProcessAsUser(IntPtr token,string app,StringBuilder line,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string directory,ref Startup startup,out ProcessInfo process);
    public static void Run() {
        IntPtr token=IntPtr.Zero,restricted=IntPtr.Zero,sid=IntPtr.Zero;
        try {
            if(!OpenProcessToken(Process.GetCurrentProcess().Handle,0xF01FF,out token))throw new Win32Exception(Marshal.GetLastWin32Error(),"OpenProcessToken");
            if(!CreateRestrictedToken(token,4,0,IntPtr.Zero,0,IntPtr.Zero,0,IntPtr.Zero,out restricted))throw new Win32Exception(Marshal.GetLastWin32Error(),"CreateRestrictedToken");
            if(!ConvertStringSidToSid("S-1-16-8192",out sid))throw new Win32Exception(Marshal.GetLastWin32Error(),"Medium SID");
            var label=new Label{Sid=sid,Attributes=0x20};
            if(!SetTokenInformation(restricted,25,ref label,Marshal.SizeOf(typeof(Label))+GetLengthSid(sid)))throw new Win32Exception(Marshal.GetLastWin32Error(),"Medium integrity");
            var startup=new Startup{Size=Marshal.SizeOf(typeof(Startup)),Desktop=@"winsta0\default"};
            var exe=Environment.GetFolderPath(Environment.SpecialFolder.System)+@"\WindowsPowerShell\v1.0\powershell.exe";
            var local=Environment.GetEnvironmentVariable("CHIPPI_SNAP_LOCAL");
            var probe=System.IO.Path.Combine(local,"probe");
            var script=System.IO.Path.Combine(probe,"probe.ps1");
            var log=System.IO.Path.Combine(local,"results","medium-output.txt");
            var command="& '"+script.Replace("'","''")+"' -SnapMedium *> '"+log.Replace("'","''")+"'";
            var line=new StringBuilder("\""+exe+"\" -NoProfile -ExecutionPolicy Bypass -Command \""+command+"\"");
            ProcessInfo process;
            if(!CreateProcessAsUser(restricted,exe,line,IntPtr.Zero,IntPtr.Zero,false,0x08000000,IntPtr.Zero,probe,ref startup,out process))throw new Win32Exception(Marshal.GetLastWin32Error(),"Create medium process");
            CloseHandle(process.Thread);
            try {
                uint waited=WaitForSingleObject(process.Process,360000);
                uint code; GetExitCodeProcess(process.Process,out code);
                System.IO.File.WriteAllText(System.IO.Path.Combine(local,"results","medium-exit.txt"),"wait="+waited+" exit="+code);
                if(waited!=0)throw new Exception("Medium Snap probe timed out");
                if(!System.IO.File.Exists(System.IO.Path.Combine(local,"results","done.txt")))
                    throw new Exception("Medium Snap probe exited "+code+" without a result");
            }
            finally { CloseHandle(process.Process); }
        }
        finally {
            if(sid!=IntPtr.Zero)LocalFree(sid);
            if(restricted!=IntPtr.Zero)CloseHandle(restricted);
            if(token!=IntPtr.Zero)CloseHandle(token);
        }
    }
}
"@
try { [SnapMedium]::Run() }
finally {
    Get-ChildItem -LiteralPath $localResults | Copy-Item -Destination C:\results -Recurse -Force
}
shutdown.exe /s /t 5
