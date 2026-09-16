using System.Diagnostics;
using System.Runtime.InteropServices;
using RemoteLAN.Protocol.Messages;

namespace RemoteLAN.Power;

public static class SystemPowerManager
{
    [Flags]
    public enum ExecutionState : uint
    {
        EsSystemRequired = 0x00000001,
        EsDisplayRequired = 0x00000002,
        EsAwayModeRequired = 0x00000040,
        EsContinuous = 0x80000000
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    private static int _keepAwakeRefCount;
    private static readonly object _lock = new();

    public static void AcquireKeepAwake()
    {
        lock (_lock)
        {
            _keepAwakeRefCount++;
            if (_keepAwakeRefCount == 1)
            {
                SetThreadExecutionState(ExecutionState.EsContinuous | ExecutionState.EsSystemRequired | ExecutionState.EsDisplayRequired);
            }
        }
    }

    public static void ReleaseKeepAwake()
    {
        lock (_lock)
        {
            if (_keepAwakeRefCount > 0)
            {
                _keepAwakeRefCount--;
                if (_keepAwakeRefCount == 0)
                {
                    SetThreadExecutionState(ExecutionState.EsContinuous);
                }
            }
        }
    }

    public static void WakeDisplay()
    {
        // Resets display and system idle timers to wake display from sleep
        SetThreadExecutionState(ExecutionState.EsSystemRequired | ExecutionState.EsDisplayRequired);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    public static bool Lock()
    {
        try
        {
            return LockWorkStation();
        }
        catch
        {
            return false;
        }
    }

    public static bool Sleep()
    {
        try
        {
            return SetSuspendState(false, true, false);
        }
        catch
        {
            return false;
        }
    }

    public static bool Restart(int delaySeconds = 2, string reason = "Restart initiated via RemoteLAN")
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("/r");
            psi.ArgumentList.Add("/t");
            psi.ArgumentList.Add(Math.Max(0, delaySeconds).ToString());
            psi.ArgumentList.Add("/f");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(reason);

            using var proc = Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Shutdown(int delaySeconds = 2, string reason = "Shutdown initiated via RemoteLAN")
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("/s");
            psi.ArgumentList.Add("/t");
            psi.ArgumentList.Add(Math.Max(0, delaySeconds).ToString());
            psi.ArgumentList.Add("/f");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(reason);

            using var proc = Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool ExecutePowerAction(PowerActionType action)
    {
        return action switch
        {
            PowerActionType.Lock => Lock(),
            PowerActionType.Sleep => Sleep(),
            PowerActionType.Restart => Restart(),
            PowerActionType.Shutdown => Shutdown(),
            _ => false
        };
    }
}
