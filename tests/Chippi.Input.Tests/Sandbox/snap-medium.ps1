# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# Windows Sandbox's logon command runs elevated. Run Snap and its target apps as an ordinary user,
# preserving the production rule that elevated windows cannot be moved. This runs only inside Sandbox.
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
            var line=new StringBuilder("\""+exe+"\" -NoProfile -ExecutionPolicy Bypass -File C:\\probe\\probe.ps1 -SnapMedium");
            ProcessInfo process;
            if(!CreateProcessAsUser(restricted,exe,line,IntPtr.Zero,IntPtr.Zero,false,0x08000000,IntPtr.Zero,@"C:\probe",ref startup,out process))throw new Win32Exception(Marshal.GetLastWin32Error(),"Create medium process");
            CloseHandle(process.Thread); CloseHandle(process.Process);
            using(var child=Process.GetProcessById(process.Pid)) {
                if(!child.WaitForExit(360000))throw new Exception("Medium Snap probe timed out");
            }
        }
        finally {
            if(sid!=IntPtr.Zero)LocalFree(sid);
            if(restricted!=IntPtr.Zero)CloseHandle(restricted);
            if(token!=IntPtr.Zero)CloseHandle(token);
        }
    }
}
"@
[SnapMedium]::Run()
