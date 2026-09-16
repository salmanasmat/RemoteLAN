using System.Runtime.InteropServices;

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
}
