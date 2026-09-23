using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using RemoteLAN.Input;

namespace RemoteLAN.Security;

public static class DesktopManager
{
    private const uint DESKTOP_ALL = 0x01FF;
    private const uint GENERIC_ALL = 0x10000000;
    private const int UOI_NAME = 2;

    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_IMPERSONATE = 0x0004;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    private const int SecurityImpersonation = 2;
    private const int TokenImpersonation = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern short VkKeyScan(char ch);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, StringBuilder pvInfo, uint nLength, out uint lpnLengthNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpTokenAttributes, int ImpersonationLevel, int TokenType, out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ImpersonateLoggedOnUser(IntPtr hToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool RevertToSelf();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("sas.dll", SetLastError = true)]
    private static extern void SendSAS(bool asUser);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(
        IntPtr hToken,
        uint dwLogonFlags,
        string? lpApplicationName,
        string? lpCommandLine,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUserW(
        IntPtr hToken,
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(
        IntPtr TokenHandle,
        int TokenInformationClass,
        ref uint TokenInformation,
        uint TokenInformationLength);

    private const int TokenSessionId = 12;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
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
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    private static bool? _isAdmin;
    private static bool _seDebugPrivilegeEnabled;
    private static readonly object _syncLock = new();

    public static bool IsAdministrator
    {
        get
        {
            if (!_isAdmin.HasValue)
            {
                try
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    _isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch
                {
                    _isAdmin = false;
                }
            }
            return _isAdmin.Value;
        }
    }

    public static bool IsSystem
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return identity.IsSystem;
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool RelaunchAsSystem(string[] args, string overrideConfigPath)
    {
        if (IsSystem || !IsAdministrator) return false;

        EnsureSeDebugPrivilege();

        int currentSessionId = Process.GetCurrentProcess().SessionId;
        var winlogonProcs = Process.GetProcessesByName("winlogon");

        foreach (var wp in winlogonProcs)
        {
            try
            {
                if (wp.SessionId != currentSessionId && winlogonProcs.Length > 1)
                {
                    continue;
                }

                IntPtr hProc = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */ | 0x0400 /* PROCESS_QUERY_INFORMATION */, false, wp.Id);
                if (hProc == IntPtr.Zero) hProc = OpenProcess(0x0400, false, wp.Id);

                if (hProc != IntPtr.Zero)
                {
                    try
                    {
                        if (OpenProcessToken(hProc, TOKEN_DUPLICATE | TOKEN_QUERY, out IntPtr hToken))
                        {
                            try
                            {
                                // TokenPrimary = 1
                                if (DuplicateTokenEx(hToken, 0x02000000 /* MAXIMUM_ALLOWED */, IntPtr.Zero, SecurityImpersonation, 1, out IntPtr hDup))
                                {
                                    try
                                    {
                                        var si = new STARTUPINFO();
                                        si.cb = Marshal.SizeOf<STARTUPINFO>();
                                        si.lpDesktop = "winsta0\\default";

                                        string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                                        if (string.IsNullOrEmpty(exePath)) continue;

                                        // Append --config path so the new instance uses the correct user's config file
                                        string cmdLine = $"\"{exePath}\"";
                                        foreach (var arg in args)
                                        {
                                            cmdLine += $" \"{arg}\"";
                                        }
                                        
                                        if (!args.Contains("--config") && !string.IsNullOrEmpty(overrideConfigPath))
                                        {
                                            cmdLine += $" --config \"{overrideConfigPath}\"";
                                        }

                                        bool result = CreateProcessWithTokenW(
                                            hDup,
                                            0, // LOGON_WITH_PROFILE = 1, but 0 is fine if we don't need profile hive
                                            null,
                                            cmdLine,
                                            0,
                                            IntPtr.Zero,
                                            null,
                                            ref si,
                                            out var pi);

                                        if (result)
                                        {
                                            CloseHandle(pi.hProcess);
                                            CloseHandle(pi.hThread);
                                            return true;
                                        }
                                        else
                                        {
                                            int err = Marshal.GetLastWin32Error();
                                            Debug.WriteLine($"[DesktopManager] CreateProcessWithTokenW failed: {err}");
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(hDup);
                                    }
                                }
                            }
                            finally
                            {
                                CloseHandle(hToken);
                            }
                        }
                    }
                    finally
                    {
                        CloseHandle(hProc);
                    }
                }
            }
            catch
            {
                // Ignore probe errors
            }
            finally
            {
                wp.Dispose();
            }
        }
        return false;
    }

    internal static Func<uint>? ActiveConsoleSessionIdOverride { get; set; }

    public static uint GetActiveConsoleSessionId()
    {
        if (ActiveConsoleSessionIdOverride != null)
        {
            return ActiveConsoleSessionIdOverride();
        }

        try
        {
            return WTSGetActiveConsoleSessionId();
        }
        catch
        {
            return 0xFFFFFFFF;
        }
    }

    /// <summary>
    /// Gets the target console or interactive logon session ID.
    /// On headless machines where no physical monitor is attached, WTSGetActiveConsoleSessionId()
    /// frequently returns 0xFFFFFFFF or 0. This method discovers active winlogon processes
    /// to determine the real interactive session.
    /// </summary>
    public static uint GetTargetConsoleSessionId()
    {
        uint consoleSessionId = GetActiveConsoleSessionId();
        if (consoleSessionId != 0xFFFFFFFF && consoleSessionId != 0)
        {
            return consoleSessionId;
        }

        try
        {
            var winlogonProcs = Process.GetProcessesByName("winlogon");
            foreach (var p in winlogonProcs)
            {
                if (p.SessionId > 0)
                {
                    return (uint)p.SessionId;
                }
            }
        }
        catch { }

        return consoleSessionId != 0xFFFFFFFF ? consoleSessionId : 1;
    }

    public static bool LaunchInConsoleSession(string[] args, string? overrideConfigPath)
    {
        EnsureSeDebugPrivilege();

        uint targetSessionId = GetTargetConsoleSessionId();
        DiagnosticLogger.Log($"[LaunchInConsoleSession] Target console session ID: {targetSessionId}");

        if (targetSessionId == 0xFFFFFFFF)
        {
            DiagnosticLogger.Log("[LaunchInConsoleSession] Unable to identify any target session.");
            return false;
        }

        IntPtr hPrimaryToken = IntPtr.Zero;

        // Strategy 1: Acquire and duplicate the primary token from winlogon.exe in target session
        var winlogonProcs = Process.GetProcessesByName("winlogon");
        foreach (var wp in winlogonProcs)
        {
            try
            {
                if (wp.SessionId != (int)targetSessionId)
                {
                    continue;
                }

                IntPtr hProc = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */ | 0x0400 /* PROCESS_QUERY_INFORMATION */, false, wp.Id);
                if (hProc == IntPtr.Zero) hProc = OpenProcess(0x0400, false, wp.Id);

                if (hProc != IntPtr.Zero)
                {
                    try
                    {
                        if (OpenProcessToken(hProc, TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY, out IntPtr hToken))
                        {
                            try
                            {
                                if (DuplicateTokenEx(hToken, 0x02000000 /* MAXIMUM_ALLOWED */, IntPtr.Zero, SecurityImpersonation, 1 /* TokenPrimary */, out IntPtr hDup))
                                {
                                    hPrimaryToken = hDup;
                                    DiagnosticLogger.Log($"[LaunchInConsoleSession] Acquired primary token from winlogon PID {wp.Id} in session {wp.SessionId}");
                                    break;
                                }
                            }
                            finally
                            {
                                CloseHandle(hToken);
                            }
                        }
                    }
                    finally
                    {
                        CloseHandle(hProc);
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Log($"[LaunchInConsoleSession] Error querying winlogon PID {wp.Id}: {ex.Message}");
            }
            finally
            {
                wp.Dispose();
            }
        }

        // Strategy 2: If winlogon token was not obtained, duplicate our current SYSTEM token and assign TokenSessionId
        if (hPrimaryToken == IntPtr.Zero)
        {
            DiagnosticLogger.Log("[LaunchInConsoleSession] Winlogon token not available; duplicating SYSTEM token and setting session ID...");
            if (OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY, out IntPtr hMyToken))
            {
                try
                {
                    if (DuplicateTokenEx(hMyToken, 0x02000000 /* MAXIMUM_ALLOWED */, IntPtr.Zero, SecurityImpersonation, 1 /* TokenPrimary */, out IntPtr hDup))
                    {
                        uint sessId = targetSessionId;
                        if (SetTokenInformation(hDup, TokenSessionId, ref sessId, sizeof(uint)))
                        {
                            hPrimaryToken = hDup;
                            DiagnosticLogger.Log($"[LaunchInConsoleSession] SetTokenInformation succeeded for session {targetSessionId}");
                        }
                        else
                        {
                            int err = Marshal.GetLastWin32Error();
                            DiagnosticLogger.Log($"[LaunchInConsoleSession] SetTokenInformation failed: {err}");
                            CloseHandle(hDup);
                        }
                    }
                }
                finally
                {
                    CloseHandle(hMyToken);
                }
            }
        }

        if (hPrimaryToken == IntPtr.Zero)
        {
            DiagnosticLogger.Log("[LaunchInConsoleSession] Failed to acquire primary token for target session.");
            return false;
        }

        try
        {
            IntPtr lpEnvironment = IntPtr.Zero;
            bool envCreated = CreateEnvironmentBlock(out lpEnvironment, hPrimaryToken, false);
            DiagnosticLogger.Log($"[LaunchInConsoleSession] CreateEnvironmentBlock result: {envCreated}");

            try
            {
                string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (string.IsNullOrEmpty(exePath))
                {
                    DiagnosticLogger.Log("[LaunchInConsoleSession] exePath is empty; aborting launch.");
                    return false;
                }

                string cmdLine = $"\"{exePath}\"";
                bool hasBackground = false;
                bool hasConsoleSession = false;

                foreach (var arg in args)
                {
                    if (arg.Equals("--background", StringComparison.OrdinalIgnoreCase)) hasBackground = true;
                    if (arg.Equals("--console-session", StringComparison.OrdinalIgnoreCase)) hasConsoleSession = true;
                    cmdLine += $" \"{arg}\"";
                }

                if (!hasBackground) cmdLine += " --background";
                if (!hasConsoleSession) cmdLine += " --console-session";

                if (!args.Contains("--config") && !string.IsNullOrEmpty(overrideConfigPath))
                {
                    cmdLine += $" --config \"{overrideConfigPath}\"";
                }

                uint creationFlags = 0;
                if (envCreated && lpEnvironment != IntPtr.Zero)
                {
                    creationFlags |= CREATE_UNICODE_ENVIRONMENT;
                }

                string workingDir = AppDomain.CurrentDomain.BaseDirectory;

                // Try desktops in order: winsta0\default, winsta0\Winlogon, or default process desktop
                string[] desktops = { @"winsta0\default", @"winsta0\Winlogon", string.Empty };
                foreach (var desktop in desktops)
                {
                    var si = new STARTUPINFO();
                    si.cb = Marshal.SizeOf<STARTUPINFO>();
                    if (!string.IsNullOrEmpty(desktop))
                    {
                        si.lpDesktop = desktop;
                    }

                    DiagnosticLogger.Log($"[LaunchInConsoleSession] Calling CreateProcessAsUserW with desktop '{desktop}'...");
                    bool result = CreateProcessAsUserW(
                        hPrimaryToken,
                        null,
                        cmdLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        creationFlags,
                        lpEnvironment,
                        workingDir,
                        ref si,
                        out var pi);

                    if (result)
                    {
                        DiagnosticLogger.Log($"[LaunchInConsoleSession] Successfully spawned console agent PID {pi.dwProcessId} into session {targetSessionId} (desktop '{desktop}')");
                        CloseHandle(pi.hProcess);
                        CloseHandle(pi.hThread);
                        return true;
                    }
                    else
                    {
                        int err = Marshal.GetLastWin32Error();
                        DiagnosticLogger.Log($"[LaunchInConsoleSession] CreateProcessAsUserW with desktop '{desktop}' failed: {err}");
                    }
                }

                // Fallback attempt: CreateProcessWithTokenW
                DiagnosticLogger.Log("[LaunchInConsoleSession] Fallback: attempting CreateProcessWithTokenW...");
                var fallbackSi = new STARTUPINFO();
                fallbackSi.cb = Marshal.SizeOf<STARTUPINFO>();
                fallbackSi.lpDesktop = @"winsta0\default";

                bool tokenResult = CreateProcessWithTokenW(
                    hPrimaryToken,
                    0,
                    null,
                    cmdLine,
                    0,
                    IntPtr.Zero,
                    null,
                    ref fallbackSi,
                    out var fallbackPi);

                if (tokenResult)
                {
                    DiagnosticLogger.Log($"[LaunchInConsoleSession] CreateProcessWithTokenW succeeded! PID {fallbackPi.dwProcessId}");
                    CloseHandle(fallbackPi.hProcess);
                    CloseHandle(fallbackPi.hThread);
                    return true;
                }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    DiagnosticLogger.Log($"[LaunchInConsoleSession] CreateProcessWithTokenW failed: {err}");
                }
            }
            finally
            {
                if (lpEnvironment != IntPtr.Zero)
                {
                    DestroyEnvironmentBlock(lpEnvironment);
                }
            }
        }
        finally
        {
            CloseHandle(hPrimaryToken);
        }

        return false;
    }

    public static void RunSessionZeroSupervisor(string[] args, string? overrideConfigPath, CancellationToken cancellationToken = default)
    {
        DiagnosticLogger.Log("=== Session 0 supervisor starting ===");
        EnsureSeDebugPrivilege();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                uint activeSessionId = GetTargetConsoleSessionId();
                if (activeSessionId == 0xFFFFFFFF)
                {
                    DiagnosticLogger.Log("[Session0Supervisor] No target session detected; sleeping 1s...");
                    Thread.Sleep(1000);
                    continue;
                }

                bool alreadyRunningInSession = false;
                try
                {
                    var procs = Process.GetProcessesByName("RemoteLAN");
                    foreach (var p in procs)
                    {
                        if (p.Id != Process.GetCurrentProcess().Id && p.SessionId == (int)activeSessionId)
                        {
                            alreadyRunningInSession = true;
                            break;
                        }
                    }
                }
                catch { }

                if (!alreadyRunningInSession)
                {
                    DiagnosticLogger.Log($"[Session0Supervisor] Agent not running in session {activeSessionId}; launching...");
                    if (!LaunchInConsoleSession(args, overrideConfigPath))
                    {
                        DiagnosticLogger.Log("[Session0Supervisor] LaunchInConsoleSession failed; retrying in 2 seconds...");
                        Thread.Sleep(2000);
                        continue;
                    }
                }

                for (int i = 0; i < 5 && !cancellationToken.IsCancellationRequested; i++)
                {
                    Thread.Sleep(1000);
                    if (GetTargetConsoleSessionId() != activeSessionId)
                    {
                        DiagnosticLogger.Log($"[Session0Supervisor] Active console session shifted from {activeSessionId} to {GetTargetConsoleSessionId()}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.LogException("Session0Supervisor unhandled loop exception", ex);
                Thread.Sleep(3000);
            }
        }

        DiagnosticLogger.Log("=== Session 0 supervisor stopped ===");
    }

    public static string GetDesktopName(IntPtr hDesktop)
    {
        if (hDesktop == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(256);
        if (GetUserObjectInformation(hDesktop, UOI_NAME, sb, (uint)sb.Capacity, out _))
        {
            return sb.ToString();
        }
        return string.Empty;
    }

    public static string GetCurrentThreadDesktopName()
    {
        IntPtr cur = GetThreadDesktop(GetCurrentThreadId());
        return GetDesktopName(cur);
    }

    public static string GetActiveInputDesktopName()
    {
        IntPtr hDesktop = OpenCurrentInputDesktop();
        if (hDesktop == IntPtr.Zero) return string.Empty;

        try
        {
            return GetDesktopName(hDesktop);
        }
        finally
        {
            CloseDesktop(hDesktop);
        }
    }

    [ThreadStatic]
    private static int t_impersonationDepth;

    [ThreadStatic]
    private static IntPtr t_attachedDesktopHandle;

    [ThreadStatic]
    private static string? t_attachedDesktopName;

    private static volatile bool _isLockScreenCached;

    public static bool IsLockScreenActiveCached => _isLockScreenCached;

    public static bool IsLockScreenActive()
    {
        IntPtr hDesk = OpenInputDesktop(0, false, 0x0001 /* DESKTOP_READOBJECTS */);
        if (hDesk != IntPtr.Zero)
        {
            string name = GetDesktopName(hDesk);
            CloseDesktop(hDesk);
            bool isLock = name.Equals("Winlogon", StringComparison.OrdinalIgnoreCase) ||
                          name.Equals("Screen-saver", StringComparison.OrdinalIgnoreCase);
            _isLockScreenCached = isLock;
            return isLock;
        }

        int err = Marshal.GetLastWin32Error();
        if (err == 5 /* ERROR_ACCESS_DENIED */)
        {
            // Access denied on OpenInputDesktop implies the active desktop is an isolated/secure desktop (Winlogon)
            _isLockScreenCached = true;
            return true;
        }

        _isLockScreenCached = false;
        return false;
    }

    public sealed class ImpersonationScope : IDisposable
    {
        private bool _disposed;
        private readonly bool _wasImpersonated;

        public ImpersonationScope()
        {
            if (IsSystem)
            {
                _wasImpersonated = false;
                return;
            }

            if (IsAdministrator)
            {
                if (t_impersonationDepth > 0)
                {
                    t_impersonationDepth++;
                    _wasImpersonated = true;
                }
                else if (TryImpersonateSystem())
                {
                    t_impersonationDepth = 1;
                    _wasImpersonated = true;
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_wasImpersonated)
                {
                    t_impersonationDepth--;
                    if (t_impersonationDepth <= 0)
                    {
                        t_impersonationDepth = 0;
                        try
                        {
                            RevertToSelf();
                        }
                        catch { }
                    }
                }
            }
        }
    }

    public static IDisposable ImpersonateSystemScope() => new ImpersonationScope();

    private static IntPtr OpenCurrentInputDesktop()
    {
        // 1. Try standard OpenInputDesktop
        IntPtr hDesk = OpenInputDesktop(0, false, DESKTOP_ALL);
        if (hDesk != IntPtr.Zero) return hDesk;

        int err = Marshal.GetLastWin32Error();
        if (err == 5 /* ERROR_ACCESS_DENIED */ && IsAdministrator)
        {
            // 2. Elevate thread token to SYSTEM via Winlogon token duplication
            using var scope = ImpersonateSystemScope();
            hDesk = OpenInputDesktop(0, false, DESKTOP_ALL);
            if (hDesk != IntPtr.Zero) return hDesk;

            // Fallback to explicit Winlogon desktop handle
            hDesk = OpenDesktop("Winlogon", 0, false, DESKTOP_ALL);
            if (hDesk != IntPtr.Zero) return hDesk;
        }

        return IntPtr.Zero;
    }

    public static bool EnsureThreadOnInputDesktop(out string currentDesktopName)
    {
        currentDesktopName = GetCurrentThreadDesktopName();
        using var scope = ImpersonateSystemScope();

        IntPtr hInputDesk = OpenInputDesktop(0, false, DESKTOP_ALL);
        if (hInputDesk == IntPtr.Zero)
        {
            hInputDesk = OpenDesktop("Winlogon", 0, false, DESKTOP_ALL);
        }

        if (hInputDesk == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            string inputName = GetDesktopName(hInputDesk);
            if (string.IsNullOrEmpty(inputName))
            {
                return false;
            }

            bool isLock = inputName.Equals("Winlogon", StringComparison.OrdinalIgnoreCase) ||
                          inputName.Equals("Screen-saver", StringComparison.OrdinalIgnoreCase);
            _isLockScreenCached = isLock;

            if (!inputName.Equals(currentDesktopName, StringComparison.OrdinalIgnoreCase))
            {
                // Active desktop has changed! Switch current thread to the input desktop
                bool switched = SetThreadDesktop(hInputDesk);
                if (switched)
                {
                    currentDesktopName = inputName;
                    if (t_attachedDesktopHandle != IntPtr.Zero && t_attachedDesktopHandle != hInputDesk)
                    {
                        CloseDesktop(t_attachedDesktopHandle);
                    }
                    t_attachedDesktopHandle = hInputDesk;
                    t_attachedDesktopName = inputName;
                    hInputDesk = IntPtr.Zero; // Keep active handle open for the assigned thread
                    return true;
                }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"[DesktopManager] SetThreadDesktop to '{inputName}' failed: {err}");
                    return false;
                }
            }

            currentDesktopName = inputName;
            return true;
        }
        finally
        {
            if (hInputDesk != IntPtr.Zero)
            {
                CloseDesktop(hInputDesk);
            }
        }
    }

    private static IntPtr _cachedSystemToken = IntPtr.Zero;

    private static bool TryImpersonateSystem()
    {
        lock (_syncLock)
        {
            if (_cachedSystemToken != IntPtr.Zero)
            {
                if (ImpersonateLoggedOnUser(_cachedSystemToken))
                {
                    return true;
                }
                CloseHandle(_cachedSystemToken);
                _cachedSystemToken = IntPtr.Zero;
            }

            EnsureSeDebugPrivilege();

            int currentSessionId = Process.GetCurrentProcess().SessionId;
            var winlogonProcs = Process.GetProcessesByName("winlogon");

            foreach (var wp in winlogonProcs)
            {
                try
                {
                    if (wp.SessionId != currentSessionId && winlogonProcs.Length > 1)
                    {
                        continue;
                    }

                    IntPtr hProc = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */ | 0x0400 /* PROCESS_QUERY_INFORMATION */, false, wp.Id);
                    if (hProc == IntPtr.Zero)
                    {
                        hProc = OpenProcess(0x0400, false, wp.Id);
                    }

                    if (hProc != IntPtr.Zero)
                    {
                        try
                        {
                            if (OpenProcessToken(hProc, TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_IMPERSONATE, out IntPtr hToken))
                            {
                                try
                                {
                                    if (DuplicateTokenEx(hToken, TOKEN_DUPLICATE | TOKEN_IMPERSONATE | TOKEN_QUERY, IntPtr.Zero, SecurityImpersonation, TokenImpersonation, out IntPtr hDup))
                                    {
                                        if (ImpersonateLoggedOnUser(hDup))
                                        {
                                            _cachedSystemToken = hDup; // Cache duplicate token for reuse
                                            return true;
                                        }
                                        CloseHandle(hDup);
                                    }
                                }
                                finally
                                {
                                    CloseHandle(hToken);
                                }
                            }
                        }
                        finally
                        {
                            CloseHandle(hProc);
                        }
                    }
                }
                catch
                {
                    // Ignore individual process probe errors
                }
                finally
                {
                    wp.Dispose();
                }
            }
        }

        return false;
    }

    private static void EnsureSeDebugPrivilege()
    {
        if (_seDebugPrivilegeEnabled) return;

        try
        {
            if (OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hMyToken))
            {
                try
                {
                    string[] privs = {
                        "SeDebugPrivilege",
                        "SeAssignPrimaryTokenPrivilege",
                        "SeIncreaseQuotaPrivilege",
                        "SeTcbPrivilege",
                        "SeImpersonatePrivilege"
                    };

                    foreach (var priv in privs)
                    {
                        if (LookupPrivilegeValue(null, priv, out LUID luid))
                        {
                            var tp = new TOKEN_PRIVILEGES
                            {
                                PrivilegeCount = 1,
                                Luid = luid,
                                Attributes = SE_PRIVILEGE_ENABLED
                            };
                            AdjustTokenPrivileges(hMyToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                        }
                    }
                    _seDebugPrivilegeEnabled = true;
                }
                finally
                {
                    CloseHandle(hMyToken);
                }
            }
        }
        catch
        {
            // Ignore privilege adjustment failures
        }
    }

    internal static Func<NativeMethods.INPUT[], uint>? SendInputOverride { get; set; }

    private static uint PerformSendInput(NativeMethods.INPUT[] inputs)
    {
        if (SendInputOverride != null)
        {
            return SendInputOverride(inputs);
        }
        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendKeyDirect(ushort virtualKey, bool keyUp)
    {
        uint flags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0;
        bool isExtended = (virtualKey >= 0x21 && virtualKey <= 0x2E) || virtualKey == 0xA3 || virtualKey == 0xA5;
        if (isExtended) flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;

        ushort scanCode = (ushort)NativeMethods.MapVirtualKey(virtualKey, NativeMethods.MAPVK_VK_TO_VSC);
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = scanCode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        uint res = PerformSendInput(new[] { input });
        if (res == 0)
        {
            int err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"[DesktopManager] SendKeyDirect VK=0x{virtualKey:X} keyUp={keyUp} failed: {err}");
        }
    }

    private static void SendKeyStrokeDirect(ushort virtualKey, int holdMs = 25)
    {
        SendKeyDirect(virtualKey, keyUp: false);
        if (holdMs > 0) Thread.Sleep(holdMs);
        SendKeyDirect(virtualKey, keyUp: true);
    }

    private static void SendUnicodeCharDirect(char c, int holdMs = 20)
    {
        var down = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = (ushort)c,
                    dwFlags = NativeMethods.KEYEVENTF_UNICODE,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };
        var up = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = (ushort)c,
                    dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        uint res1 = PerformSendInput(new[] { down });
        if (holdMs > 0) Thread.Sleep(holdMs);
        uint res2 = PerformSendInput(new[] { up });
        if (res1 == 0 || res2 == 0)
        {
            int err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"[DesktopManager] SendUnicodeCharDirect char='{c}' failed: {err}");
        }
    }

    private static void SendCharAsKeyStroke(char c, int holdMs = 25)
    {
        short vkScan = VkKeyScan(c);
        if (vkScan != -1)
        {
            byte vk = (byte)(vkScan & 0xFF);
            byte shiftState = (byte)((vkScan >> 8) & 0xFF);

            bool needShift = (shiftState & 1) != 0;
            bool needCtrl = (shiftState & 2) != 0;
            bool needAlt = (shiftState & 4) != 0;

            if (needCtrl) SendKeyDirect(0x11 /* VK_CONTROL */, keyUp: false);
            if (needAlt) SendKeyDirect(0x12 /* VK_MENU */, keyUp: false);
            if (needShift) SendKeyDirect(0x10 /* VK_SHIFT */, keyUp: false);

            if (needCtrl || needAlt || needShift) Thread.Sleep(10);

            SendKeyStrokeDirect(vk, holdMs);

            if (needCtrl || needAlt || needShift) Thread.Sleep(10);

            if (needShift) SendKeyDirect(0x10 /* VK_SHIFT */, keyUp: true);
            if (needAlt) SendKeyDirect(0x12 /* VK_MENU */, keyUp: true);
            if (needCtrl) SendKeyDirect(0x11 /* VK_CONTROL */, keyUp: true);
        }
        else
        {
            SendUnicodeCharDirect(c, holdMs);
        }
    }

    public static void SendCtrlAltDel()
    {
        if (SendInputOverride != null)
        {
            SendKeyStrokeDirect(0x1B /* VK_ESCAPE */, 5);
            SendKeyStrokeDirect(0x26 /* VK_UP */, 5);
            return;
        }

        // 1. Try SendSAS from sas.dll if permitted
        try
        {
            SendSAS(false);
            return;
        }
        catch
        {
            // Fall back to software lock screen wake & sequence
        }

        // 2. Ensure thread is on the active input desktop
        using var scope = ImpersonateSystemScope();
        EnsureThreadOnInputDesktop(out _);

        // 3. Dismiss lock screen wallpaper & wake password prompt safely
        SendKeyStrokeDirect(0x1B /* VK_ESCAPE */, 30);
        Thread.Sleep(50);
        SendKeyStrokeDirect(0x26 /* VK_UP */, 30);
        Thread.Sleep(250);
    }

    /// <summary>
    /// Programmatically wakes the Windows lock screen and automatically inputs the provided OS password
    /// to authenticate the LogonUI credential provider and unlock the session.
    /// </summary>
    public static bool UnlockWithPassword(string password)
    {
        if (string.IsNullOrEmpty(password)) return false;

        using var scope = ImpersonateSystemScope();
        EnsureThreadOnInputDesktop(out string desktopName);
        Debug.WriteLine($"[DesktopManager] UnlockWithPassword starting on desktop: '{desktopName}'");

        // 1. First attempt official Secure Attention Sequence (SAS) if permitted (instant reliable wake)
        bool sasSent = false;
        if (SendInputOverride == null)
        {
            try
            {
                SendSAS(false);
                sasSent = true;
                Thread.Sleep(350);
            }
            catch
            {
                // Fall back to software wake
            }
        }

        // 2. Software wake: Slide the lock screen curtain up to reveal LogonUI without typing a printable character.
        // VK_UP (with extended key flag) slides the curtain up safely.
        if (!sasSent)
        {
            SendKeyStrokeDirect(0x26 /* VK_UP */, 30);
            Thread.Sleep(500);
        }

        // Re-ensure desktop handle in case LogonUI transitioned desktops
        EnsureThreadOnInputDesktop(out _);

        // 3. Ensure CapsLock is OFF so password character casing is not inverted
        try
        {
            if ((NativeMethods.GetKeyState(0x14 /* VK_CAPITAL */) & 1) != 0)
            {
                SendKeyStrokeDirect(0x14 /* VK_CAPITAL */, 25);
                Thread.Sleep(50);
            }
        }
        catch { }

        // 4. Clear any existing characters in the password box safely.
        // DO NOT send VK_ESCAPE here because on Windows 10/11, ESCAPE on the login screen
        // slides the curtain back DOWN to the wallpaper clock!
        for (int i = 0; i < 30; i++)
        {
            SendKeyStrokeDirect(0x08 /* VK_BACK */, 10);
            Thread.Sleep(5);
        }
        Thread.Sleep(100);

        // 5. Send the password characters using simulated hardware keystrokes (VK + scan code + Shift/Ctrl/Alt)
        // so LogonUI credential provider receives full WM_KEYDOWN scan code fidelity
        foreach (char c in password)
        {
            SendCharAsKeyStroke(c, holdMs: 25);
            Thread.Sleep(30);
        }

        // 6. Submit the password by sending Enter after the full password has been entered
        Thread.Sleep(200);
        SendKeyStrokeDirect(0x0D /* VK_RETURN */, 50);
        Thread.Sleep(300);

        return true;
    }
}
