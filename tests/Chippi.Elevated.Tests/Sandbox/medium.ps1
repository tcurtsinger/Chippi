# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors
param([ValidateSet('--preflight','--handshake','--guard','--sandbox','--app-updates','--tools')][string]$Mode)
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or !(Test-Path 'C:\chippi\Chippi.exe')) { throw 'Windows Sandbox only.' }
Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class AdminMedium {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {
        public int Size; public string Reserved,Desktop,Title;
        public int X,Y,Width,Height,CharsX,CharsY,Fill,Flags; public short Show,ReservedCount;
        public IntPtr ReservedData,Input,Output,Error;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process,Thread; public int Pid,Tid; }
    [StructLayout(LayoutKind.Sequential)] struct Label { public IntPtr Sid; public uint Attributes; }
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool CreateRestrictedToken(IntPtr source,uint flags,uint disabled,IntPtr sids,uint privileges,IntPtr privilegeList,uint restricted,IntPtr restrictedList,out IntPtr token);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ConvertStringSidToSid(string s,out IntPtr sid);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool SetTokenInformation(IntPtr token,int cls,ref Label info,int size);
    [DllImport("advapi32.dll")] static extern int GetLengthSid(IntPtr sid);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h,uint ms);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h,out uint code);
    [DllImport("advapi32.dll",EntryPoint="CreateProcessAsUserW",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool CreateProcessAsUser(IntPtr token,string app,StringBuilder line,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string directory,ref Startup startup,out ProcessInfo process);
    static void Check(bool success,string step) { if(!success) { int code=Marshal.GetLastWin32Error(); throw new Win32Exception(code,step+" ("+code+")"); } }
    public static void Run(string mode) {
        if(mode!="--preflight" && mode!="--handshake" && mode!="--guard" && mode!="--sandbox" && mode!="--app-updates" && mode!="--tools")throw new ArgumentException("Invalid probe mode");
        IntPtr original=IntPtr.Zero,token=IntPtr.Zero,sid=IntPtr.Zero;
        var helpers=new System.Collections.Generic.List<System.Diagnostics.Process>();
        try {
            Check(OpenProcessToken(GetCurrentProcess(),0xF01FF,out original),"Open guest token");
            Check(CreateRestrictedToken(original,5,0,IntPtr.Zero,0,IntPtr.Zero,0,IntPtr.Zero,out token),"Create LUA token");
            Check(ConvertStringSidToSid("S-1-16-8192",out sid),"Medium SID");
            var label=new Label{Sid=sid,Attributes=0x20};
            Check(SetTokenInformation(token,25,ref label,Marshal.SizeOf(typeof(Label))+GetLengthSid(sid)),"Medium integrity");
            var startup=new Startup{Size=Marshal.SizeOf(typeof(Startup)),Desktop=@"winsta0\default"};
            var line=new StringBuilder("\"C:\\chippi\\Chippi.exe\" "+mode);
            ProcessInfo process;
            Check(CreateProcessAsUser(token,@"C:\chippi\Chippi.exe",line,IntPtr.Zero,IntPtr.Zero,false,0,IntPtr.Zero,@"C:\chippi",ref startup,out process),"Create medium probe");
            CloseHandle(process.Thread);
            try {
                uint waited=258;
                var deadline=DateTime.UtcNow.AddMinutes(mode=="--tools"?15:5);
                int bound=mode=="--tools"?10:4;
                while((waited=WaitForSingleObject(process.Process,50))==258 && DateTime.UtcNow<deadline) {
                    foreach(var request in System.IO.Directory.GetFiles(@"C:\results","broker-*.request")) {
                        var pipe=System.IO.File.ReadAllText(request).Trim();
                        if(!System.Text.RegularExpressions.Regex.IsMatch(pipe,@"\Achippi-admin-[0-9a-f]{32}\z"))throw new Exception("Invalid broker pipe");
                        if(helpers.Count>=bound)throw new Exception("Too many helper requests");
                        var helper=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(@"C:\chippi\Chippi.Elevated.exe","--pipe "+pipe){UseShellExecute=false,CreateNoWindow=true});
                        helpers.Add(helper);
                        var response=@"C:\results\"+pipe+".pid";
                        System.IO.File.WriteAllText(response+".tmp",helper.Id.ToString());
                        System.IO.File.Move(response+".tmp",response);
                        System.IO.File.Delete(request);
                    }
                }
                uint code; Check(GetExitCodeProcess(process.Process,out code),"Read probe exit");
                System.IO.File.WriteAllText(@"C:\results\medium-"+mode.Substring(2)+".txt","wait="+waited+" exit="+code);
                if(waited!=0 || code!=0)throw new Exception("Medium probe failed: wait="+waited+" exit="+code);
            } finally { CloseHandle(process.Process); }
        } finally {
            foreach(var helper in helpers)helper.Dispose();
            if(sid!=IntPtr.Zero)LocalFree(sid); if(token!=IntPtr.Zero)CloseHandle(token); if(original!=IntPtr.Zero)CloseHandle(original);
        }
    }
}
'@
[AdminMedium]::Run($Mode)
