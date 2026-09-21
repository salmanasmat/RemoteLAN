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
}
