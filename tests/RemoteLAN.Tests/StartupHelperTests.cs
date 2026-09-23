using System;
using Microsoft.Win32;
using RemoteLAN.Security;
using Xunit;

namespace RemoteLAN.Tests;

public class StartupHelperTests
{
    [Fact]
    public void StartupHelper_Constants_AreWellFormed()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", StartupHelper.RunKeyPath);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", StartupHelper.StartupApprovedPath);
        Assert.Equal("RemoteLAN", StartupHelper.AppName);
        Assert.Equal("RemoteLAN_Autostart", StartupHelper.TaskName);
    }

    [Fact]
    public void StartupApproved_DisabledState_DetectionWorks()
    {
        // Test with simulated disabled byte arrays
        byte[] disabledBytes = new byte[] { 0x03, 0x00, 0x00, 0x00 };
        byte[] enabledBytes = new byte[] { 0x02, 0x00, 0x00, 0x00 };

        Assert.True((disabledBytes[0] & 1) != 0, "0x03 should be detected as disabled");
        Assert.False((enabledBytes[0] & 1) != 0, "0x02 should not be detected as disabled");
    }

    [Fact]
    public void IsRunAtStartupEnabled_DoesNotThrow()
    {
        // Should evaluate without crashing or throwing unhandled exceptions
        var exception = Record.Exception(() => StartupHelper.IsRunAtStartupEnabled());
        Assert.Null(exception);
    }

    [Fact]
    public void IsTaskScheduled_DoesNotThrow()
    {
        var exception = Record.Exception(() => StartupHelper.IsTaskScheduled());
        Assert.Null(exception);
    }

    [Fact]
    public void NetworkAddressItem_BadgeAndIcon_ResolvedCorrectly()
    {
        var ethernetItem = new MainWindow.NetworkAddressItem
        {
            IpAddress = "192.168.1.10",
            InterfaceName = "Ethernet",
            InterfaceType = "Ethernet",
            IsPrimary = true
        };

        Assert.False(ethernetItem.IsWifi);
        Assert.False(ethernetItem.IsLoopback);
        Assert.Equal("Ethernet", ethernetItem.BadgeText);
        Assert.Equal("#EFF6FF", ethernetItem.BadgeBackgroundBrush);
        Assert.Equal("#2563EB", ethernetItem.BadgeForegroundBrush);
        Assert.NotEmpty(ethernetItem.IconData);

        var wifiItem = new MainWindow.NetworkAddressItem
        {
            IpAddress = "192.168.1.25",
            InterfaceName = "Wi-Fi",
            InterfaceType = "Wi-Fi",
            IsPrimary = false
        };

        Assert.True(wifiItem.IsWifi);
        Assert.Equal("Wi-Fi", wifiItem.BadgeText);
        Assert.Equal("#F0FDF4", wifiItem.BadgeBackgroundBrush);
        Assert.Equal("#16A34A", wifiItem.BadgeForegroundBrush);

        var loopbackItem = new MainWindow.NetworkAddressItem
        {
            IpAddress = "127.0.0.1",
            InterfaceName = "Loopback",
            InterfaceType = "Loopback",
            IsPrimary = false
        };

        Assert.True(loopbackItem.IsLoopback);
        Assert.Equal("Local", loopbackItem.BadgeText);

        var offlineItem = new MainWindow.NetworkAddressItem
        {
            IpAddress = "Offline",
            InterfaceName = "No active connection",
            InterfaceType = "Offline",
            IsPrimary = false
        };

        Assert.True(offlineItem.IsOffline);
        Assert.Equal("Offline", offlineItem.BadgeText);
        Assert.Equal("#F1F5F9", offlineItem.BadgeBackgroundBrush);
        Assert.Equal("#94A3B8", offlineItem.BadgeForegroundBrush);
    }

    [Fact]
    public void EvaluateNetworkInterfaces_WhenBothEthernetAndWifiAvailable_SelectsEthernetAsDefault()
    {
        var interfaces = new List<MainWindow.DetectedInterfaceInfo>
        {
            new("Wi-Fi Adapter", "Intel(R) Wi-Fi 6 AX201", System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211,
                System.Net.NetworkInformation.OperationalStatus.Up, true, new[] { "192.168.1.25" }),
            new("Ethernet", "Realtek PCIe GbE Family Controller", System.Net.NetworkInformation.NetworkInterfaceType.Ethernet,
                System.Net.NetworkInformation.OperationalStatus.Up, true, new[] { "192.168.1.10" })
        };

        var (items, hasActive) = MainWindow.EvaluateNetworkInterfaces(interfaces);

        Assert.True(hasActive);
        Assert.Equal(2, items.Count);
        // Ethernet must be sorted first (default) and marked Primary
        Assert.Equal("192.168.1.10", items[0].IpAddress);
        Assert.Equal("Ethernet", items[0].InterfaceType);
        Assert.True(items[0].IsPrimary);
        Assert.Equal("Ethernet", items[0].BadgeText);

        // Wi-Fi is second and not primary
        Assert.Equal("192.168.1.25", items[1].IpAddress);
        Assert.Equal("Wi-Fi", items[1].InterfaceType);
        Assert.False(items[1].IsPrimary);
        Assert.Equal("Wi-Fi", items[1].BadgeText);
    }

    [Fact]
    public void EvaluateNetworkInterfaces_WhenOnlyWifiAvailable_SelectsWifiAsDefault()
    {
        var interfaces = new List<MainWindow.DetectedInterfaceInfo>
        {
            new("Wi-Fi Adapter", "Intel(R) Wi-Fi 6 AX201", System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211,
                System.Net.NetworkInformation.OperationalStatus.Up, true, new[] { "192.168.1.45" })
        };

        var (items, hasActive) = MainWindow.EvaluateNetworkInterfaces(interfaces);

        Assert.True(hasActive);
        Assert.Single(items);
        Assert.Equal("192.168.1.45", items[0].IpAddress);
        Assert.Equal("Wi-Fi", items[0].InterfaceType);
        Assert.True(items[0].IsPrimary);
        Assert.Equal("Wi-Fi", items[0].BadgeText);
    }

    [Fact]
    public void EvaluateNetworkInterfaces_WhenOnlyEthernetAvailable_SelectsEthernetAsDefault()
    {
        var interfaces = new List<MainWindow.DetectedInterfaceInfo>
        {
            new("Ethernet", "Intel(R) Ethernet Connection", System.Net.NetworkInformation.NetworkInterfaceType.Ethernet,
                System.Net.NetworkInformation.OperationalStatus.Up, true, new[] { "10.0.0.5" })
        };

        var (items, hasActive) = MainWindow.EvaluateNetworkInterfaces(interfaces);

        Assert.True(hasActive);
        Assert.Single(items);
        Assert.Equal("10.0.0.5", items[0].IpAddress);
        Assert.Equal("Ethernet", items[0].InterfaceType);
        Assert.True(items[0].IsPrimary);
        Assert.Equal("Ethernet", items[0].BadgeText);
    }

    [Fact]
    public void EvaluateNetworkInterfaces_WhenNeitherAvailable_ReturnsInactiveAndEmpty()
    {
        var interfaces = new List<MainWindow.DetectedInterfaceInfo>
        {
            // Adapter down
            new("Ethernet", "Realtek GbE", System.Net.NetworkInformation.NetworkInterfaceType.Ethernet,
                System.Net.NetworkInformation.OperationalStatus.Down, false, new[] { "192.168.1.10" }),
            // Loopback only
            new("Loopback Pseudo-Interface", "Loopback", System.Net.NetworkInformation.NetworkInterfaceType.Loopback,
                System.Net.NetworkInformation.OperationalStatus.Up, false, new[] { "127.0.0.1" }),
            // APIPA only
            new("Wi-Fi", "Intel Wi-Fi", System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211,
                System.Net.NetworkInformation.OperationalStatus.Up, false, new[] { "169.254.12.34" })
        };

        var (items, hasActive) = MainWindow.EvaluateNetworkInterfaces(interfaces);

        Assert.False(hasActive);
        Assert.Empty(items);
    }

    [Fact]
    public void AgentEndpointInfo_BadgeAndIcon_ResolvedCorrectly()
    {
        var ethernetEndpoint = new Protocol.Discovery.AgentEndpointInfo
        {
            IpAddress = "192.168.1.50",
            InterfaceType = "Ethernet"
        };

        Assert.True(ethernetEndpoint.IsEthernet);
        Assert.NotEmpty(ethernetEndpoint.IconData);
        Assert.Equal("#2563EB", ethernetEndpoint.IconBrush);

        var wifiEndpoint = new Protocol.Discovery.AgentEndpointInfo
        {
            IpAddress = "192.168.1.60",
            InterfaceType = "Wi-Fi"
        };

        Assert.False(wifiEndpoint.IsEthernet);
        Assert.NotEmpty(wifiEndpoint.IconData);
        Assert.Equal("#16A34A", wifiEndpoint.IconBrush);
    }

    [Fact]
    public void DiagnosticLogger_WritesLogEntries_WithoutThrowing()
    {
        string tempLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"remotelan_test_log_{Guid.NewGuid():N}.log");
        try
        {
            DiagnosticLogger.LogFilePath = tempLog;
            DiagnosticLogger.Log("Test log entry 1");
            DiagnosticLogger.LogException("Test context", new InvalidOperationException("Test exception message"));

            Assert.True(System.IO.File.Exists(tempLog));
            string content = System.IO.File.ReadAllText(tempLog);
            Assert.Contains("Test log entry 1", content);
            Assert.Contains("Test exception message", content);
        }
        finally
        {
            try { System.IO.File.Delete(tempLog); } catch { }
            DiagnosticLogger.LogFilePath = null!;
        }
    }

    [Fact]
    public void GetTargetConsoleSessionId_RespectsOverride_AndResolvesValidSession()
    {
        try
        {
            // Case 1: Active console session is valid (e.g. session 2)
            DesktopManager.ActiveConsoleSessionIdOverride = () => 2;
            uint targetSession = DesktopManager.GetTargetConsoleSessionId();
            Assert.Equal(2u, targetSession);

            // Case 2: Headless PC scenario (WTSGetActiveConsoleSessionId returns 0xFFFFFFFF)
            DesktopManager.ActiveConsoleSessionIdOverride = () => 0xFFFFFFFF;
            uint headlessSession = DesktopManager.GetTargetConsoleSessionId();
            // Must return a valid session (> 0) and NEVER 0xFFFFFFFF
            Assert.True(headlessSession > 0 && headlessSession != 0xFFFFFFFF);
        }
        finally
        {
            DesktopManager.ActiveConsoleSessionIdOverride = null;
        }
    }
}

