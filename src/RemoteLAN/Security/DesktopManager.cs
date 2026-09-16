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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

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

    [DllImport("sas.dll", SetLastError = true)]
    private static extern void SendSAS(bool asUser);

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

    public static bool IsLockScreenActive()
    {
        string name = GetActiveInputDesktopName();
        return name.Equals("Winlogon", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Screen-saver", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class ImpersonationScope : IDisposable
    {
        private bool _disposed;
        private readonly bool _wasImpersonated;

        public ImpersonationScope()
        {
            if (IsAdministrator)
            {
                _wasImpersonated = TryImpersonateSystem();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_wasImpersonated)
                {
                    try
                    {
                        RevertToSelf();
                    }
                    catch { }
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
            if (TryImpersonateSystem())
            {
                try
                {
                    hDesk = OpenInputDesktop(0, false, DESKTOP_ALL);
                    if (hDesk != IntPtr.Zero) return hDesk;

                    // Fallback to explicit Winlogon desktop handle
                    hDesk = OpenDesktop("Winlogon", 0, false, DESKTOP_ALL);
                    if (hDesk != IntPtr.Zero) return hDesk;
                }
                finally
                {
                    RevertToSelf();
                }
            }
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

            if (!inputName.Equals(currentDesktopName, StringComparison.OrdinalIgnoreCase))
            {
                // Active desktop has changed! Switch current thread to the input desktop
                // while still maintaining SYSTEM impersonation if available.
                bool switched = SetThreadDesktop(hInputDesk);
                if (switched)
                {
                    currentDesktopName = inputName;
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
            CloseDesktop(hInputDesk);
        }
    }

    private static bool TryImpersonateSystem()
    {
        lock (_syncLock)
        {
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
                                    if (DuplicateTokenEx(hToken, GENERIC_ALL, IntPtr.Zero, SecurityImpersonation, TokenImpersonation, out IntPtr hDup))
                                    {
                                        try
                                        {
                                            if (ImpersonateLoggedOnUser(hDup))
                                            {
                                                return true;
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
                    if (LookupPrivilegeValue(null, "SeDebugPrivilege", out LUID luid))
                    {
                        var tp = new TOKEN_PRIVILEGES
                        {
                            PrivilegeCount = 1,
                            Luid = luid,
                            Attributes = SE_PRIVILEGE_ENABLED
                        };
                        AdjustTokenPrivileges(hMyToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                        _seDebugPrivilegeEnabled = true;
                    }
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

    public static void SendCtrlAltDel()
    {
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
        EnsureThreadOnInputDesktop(out _);

        // 3. Dismiss lock screen wallpaper & wake password prompt:
        // Simulate Space / Enter key to slide up Windows lock screen wallpaper
        using (var injector = new InputInjector())
        {
            injector.InjectKeyboardKey(0x20 /* VK_SPACE */, Protocol.Messages.KeyAction.Down, false);
            injector.InjectKeyboardKey(0x20 /* VK_SPACE */, Protocol.Messages.KeyAction.Up, false);
            Thread.Sleep(100);
            injector.InjectKeyboardKey(0x0D /* VK_RETURN */, Protocol.Messages.KeyAction.Down, false);
            injector.InjectKeyboardKey(0x0D /* VK_RETURN */, Protocol.Messages.KeyAction.Up, false);
            Thread.Sleep(100);
        }
    }
}
