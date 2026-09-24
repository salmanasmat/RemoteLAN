using System.ComponentModel;
using System.Diagnostics;
using RemoteLAN.Security;

namespace RemoteLAN.Security;

public static class FirewallHelper
{
    public static bool IsRulePresent(int port = 8443)
    {
        try
        {
            string ruleName = $"RemoteLAN WebBridge (TCP {port})";

            var checkPsi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall show rule name=\"{ruleName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            using var checkProc = Process.Start(checkPsi);
            string output = checkProc?.StandardOutput.ReadToEnd() ?? "";
            checkProc?.WaitForExit(2000);

            return output.Contains(ruleName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void EnsureWebBridgePortOpen(int port = 8443)
    {
        try
        {
            if (IsRulePresent(port)) return;

            if (DesktopManager.IsAdministrator || DesktopManager.IsSystem)
            {
                string ruleName = $"RemoteLAN WebBridge (TCP {port})";
                var addPsi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port} profile=any description=\"Allows incoming browser remote control via RemoteLAN WebBridge\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var addProc = Process.Start(addPsi);
                addProc?.WaitForExit(3000);

                DiagnosticLogger.Log($"[FirewallHelper] Added inbound firewall rule for port {port} on all profiles.");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"[FirewallHelper] Could not configure firewall: {ex.Message}");
        }
    }

    public static bool RequestAddFirewallRuleElevated(int port = 8443)
    {
        try
        {
            string ruleName = $"RemoteLAN WebBridge (TCP {port})";
            var addPsi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port} profile=any description=\"Allows incoming browser remote control via RemoteLAN WebBridge\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var addProc = Process.Start(addPsi);
            addProc?.WaitForExit(5000);

            return IsRulePresent(port);
        }
        catch (Win32Exception)
        {
            // User cancelled UAC prompt
            return false;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"[FirewallHelper] Failed to request elevated firewall rule: {ex.Message}");
            return false;
        }
    }
}
