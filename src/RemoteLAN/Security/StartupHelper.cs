using Microsoft.Win32;

namespace RemoteLAN.Security;

public static class StartupHelper
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string StartupApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string AppName = "RemoteLAN";
    public const string TaskName = "RemoteLAN_Autostart";

    /// <summary>
    /// Checks whether startup is enabled in registry or Task Scheduler,
    /// while also respecting Windows Task Manager's StartupApproved disabled state.
    /// </summary>
    public static bool IsRunAtStartupEnabled()
    {
        try
        {
            // 1. Check if user disabled it in Windows Task Manager
            if (IsStartupDisabledInTaskApproved())
            {
                return false;
            }

            // 2. Check if registered in CurrentUser Run key
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            bool runKeyExists = key?.GetValue(AppName) != null;

            if (runKeyExists)
            {
                return true;
            }

            // 3. Check if registered in Windows Task Scheduler
            return IsTaskScheduled();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if Windows Task Manager or Settings has marked this startup entry as disabled.
    /// </summary>
    public static bool IsStartupDisabledInTaskApproved()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedPath, false);
            if (key?.GetValue(AppName) is byte[] bytes && bytes.Length > 0)
            {
                // In StartupApproved\Run, if the first byte has the low bit set (e.g. 0x03),
                // it represents Disabled in Windows Task Manager. 0x02 represents Enabled.
                return (bytes[0] & 1) != 0;
            }
        }
        catch
        {
            // Ignore registry read errors
        }
        return false;
    }

    /// <summary>
    /// Verifies if the elevated Scheduled Task exists in Windows Task Scheduler.
    /// </summary>
    public static bool IsTaskScheduled()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = System.Diagnostics.Process.Start(startInfo);
            if (proc != null)
            {
                proc.WaitForExit(3000);
                return proc.ExitCode == 0;
            }
        }
        catch
        {
            // Ignore process execution errors
        }
        return false;
    }

    /// <summary>
    /// Enables or disables automatic startup with Windows.
    /// Manages both HKCU Run (for Task Manager/Settings visibility) and
    /// Windows Task Scheduler (for elevated, silent background launch on logon).
    /// </summary>
    public static void SetRunAtStartup(bool enable, string? customExePath = null)
    {
        try
        {
            if (enable)
            {
                string exePath = customExePath ?? Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath))
                {
                    // 1. Set HKCU Run key for Windows Startup list visibility
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    {
                        key?.SetValue(AppName, $"\"{exePath}\" --background");
                    }

                    // 2. Mark Enabled in StartupApproved\Run
                    using (var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedPath, true))
                    {
                        if (approvedKey != null)
                        {
                            byte[] enabledBytes = new byte[12] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                            approvedKey.SetValue(AppName, enabledBytes, RegistryValueKind.Binary);
                        }
                    }

                    // 3. Create or update elevated Task Scheduler job
                    CreateScheduledTask(exePath);
                }
            }
            else
            {
                // 1. Remove from HKCU Run key
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    key?.DeleteValue(AppName, false);
                }

                // 2. Remove Task Scheduler job
                DeleteScheduledTask();
            }
        }
        catch
        {
            // Silently ignore permissions or policy errors
        }
    }

    /// <summary>
    /// Creates or updates the Task Scheduler task to run at logon with highest privileges,
    /// enabling execution on battery and disabling timeout limits.
    /// </summary>
    public static void CreateScheduledTask(string exePath)
    {
        try
        {
            // Escape the inner path inside the /TR quoted string
            string escapedArgs = $"/Create /F /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --background\" /SC ONLOGON /RL HIGHEST";
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = escapedArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = System.Diagnostics.Process.Start(startInfo);
            proc?.WaitForExit(5000);

            // Configure task settings to allow battery operation and disable execution timeout
            ConfigureTaskSettings();
        }
        catch
        {
            // Ignore execution errors
        }
    }

    /// <summary>
    /// Uses PowerShell to configure scheduled task settings so it starts on battery
    /// and doesn't time out after 72 hours.
    /// </summary>
    private static void ConfigureTaskSettings()
    {
        try
        {
            string psCmd = $"$t = Get-ScheduledTask -TaskName '{TaskName}' -ErrorAction SilentlyContinue; if ($t) {{ $t.Settings.DisallowStartIfOnBatteries = $false; $t.Settings.StopIfGoingOnBatteries = $false; $t.Settings.ExecutionTimeLimit = 'PT0S'; Set-ScheduledTask $t }}";
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCmd}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = System.Diagnostics.Process.Start(startInfo);
            proc?.WaitForExit(5000);
        }
        catch
        {
            // Ignore configuration errors
        }
    }

    /// <summary>
    /// Deletes the scheduled task from Windows Task Scheduler.
    /// </summary>
    public static void DeleteScheduledTask()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Delete /F /TN \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = System.Diagnostics.Process.Start(startInfo);
            proc?.WaitForExit(5000);
        }
        catch
        {
            // Ignore errors
        }
    }

    /// <summary>
    /// Auto-heals scheduled task registration if startup is configured as enabled
    /// but the scheduled task is missing.
    /// </summary>
    public static void EnsureStartupSynchronized()
    {
        try
        {
            if (IsRunAtStartupEnabled() && !IsTaskScheduled())
            {
                string exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath))
                {
                    CreateScheduledTask(exePath);
                }
            }
        }
        catch
        {
            // Non-critical auto-healing
        }
    }
}
