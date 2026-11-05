# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# Sandbox's LogonCommand and explorer run elevated. Use the measured SAFER+medium path
# so Snap can retain its production elevated-window refusal.
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or -not (Test-Path -LiteralPath 'C:\chippi\Chippi.exe')) {
    throw 'This launcher runs only inside the Windows Sandbox check'
}
Add-Type @"
using System;
using System.ComponentModel;
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
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool SaferCreateLevel(uint scope,uint level,uint flags,out IntPtr handle,IntPtr reserved);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool SaferComputeTokenFromLevel(IntPtr level,IntPtr source,out IntPtr token,uint flags,IntPtr reserved);
    [DllImport("advapi32.dll")] static extern bool SaferCloseLevel(IntPtr level);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ConvertStringSidToSid(string s,out IntPtr sid);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool SetTokenInformation(IntPtr token,int cls,ref Label info,int size);
    [DllImport("advapi32.dll")] static extern int GetLengthSid(IntPtr sid);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h,uint ms);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h,out uint code);
    [DllImport("advapi32.dll",EntryPoint="CreateProcessAsUserW",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool CreateProcessAsUser(IntPtr token,string app,StringBuilder line,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string directory,ref Startup startup,out ProcessInfo process);
    static void Check(bool success,string step) {
        if(!success)throw new Win32Exception(Marshal.GetLastWin32Error(),step);
    }
    public static void Run() {
        IntPtr level=IntPtr.Zero,token=IntPtr.Zero,sid=IntPtr.Zero;
        try {
            Check(SaferCreateLevel(2,0x20000,1,out level,IntPtr.Zero),"SaferCreateLevel");
            Check(SaferComputeTokenFromLevel(level,IntPtr.Zero,out token,0,IntPtr.Zero),"SaferComputeTokenFromLevel");
            Check(ConvertStringSidToSid("S-1-16-8192",out sid),"Medium SID");
            var label=new Label{Sid=sid,Attributes=0x20};
            Check(SetTokenInformation(token,25,ref label,Marshal.SizeOf(typeof(Label))+GetLengthSid(sid)),"Medium integrity");
            var startup=new Startup{Size=Marshal.SizeOf(typeof(Startup)),Desktop=@"winsta0\default"};
            var exe=Environment.GetFolderPath(Environment.SpecialFolder.System)+@"\WindowsPowerShell\v1.0\powershell.exe";
            var line=new StringBuilder("\""+exe+"\" -NoProfile -ExecutionPolicy Bypass -File C:\\probe\\probe.ps1 -SnapMedium");
            ProcessInfo process;
            // Flags 0 shares the existing guest console. CREATE_NO_WINDOW failed GUI startup in this image.
            Check(CreateProcessAsUser(token,null,line,IntPtr.Zero,IntPtr.Zero,false,0,IntPtr.Zero,@"C:\probe",ref startup,out process),"Create medium process");
            CloseHandle(process.Thread);
            try {
                uint waited=WaitForSingleObject(process.Process,300000);
                uint code; GetExitCodeProcess(process.Process,out code);
                System.IO.File.WriteAllText(@"C:\results\medium-exit.txt","wait="+waited+" exit="+code);
                if(waited!=0)throw new Exception("Medium Snap probe timed out");
                if(!System.IO.File.Exists(@"C:\results\done.txt"))throw new Exception("Medium Snap probe exited "+code+" without a result");
            }
            finally { CloseHandle(process.Process); }
        }
        finally {
            if(sid!=IntPtr.Zero)LocalFree(sid);
            if(token!=IntPtr.Zero)CloseHandle(token);
            if(level!=IntPtr.Zero)SaferCloseLevel(level);
        }
    }
}
"@
[SnapMedium]::Run()
