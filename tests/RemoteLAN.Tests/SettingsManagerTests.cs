using System.IO;
using RemoteLAN.Security;

namespace RemoteLAN.Tests;

public class SettingsManagerTests : IDisposable
{
    private readonly string _tempSettingsPath;

    public SettingsManagerTests()
    {
        _tempSettingsPath = Path.Combine(Path.GetTempPath(), $"remotelan_test_settings_{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempSettingsPath))
            {
                File.Delete(_tempSettingsPath);
            }
        }
        catch { }
    }

    [Fact]
    public void SettingsManager_HostPin_Persists_And_Loads()
    {
        var manager1 = new SettingsManager(_tempSettingsPath);
        Assert.Null(manager1.GetHostPin());

        manager1.SaveHostPin("876543");
        Assert.Equal("876543", manager1.GetHostPin());

        // Reload from disk in a fresh instance
        var manager2 = new SettingsManager(_tempSettingsPath);
        Assert.Equal("876543", manager2.GetHostPin());
    }

    [Fact]
    public void SettingsManager_SavePassword_And_TryGetPassword_ByMachineName_And_Ip()
    {
        var manager = new SettingsManager(_tempSettingsPath);

        manager.SavePassword("DESKTOP-OFFICE", "192.168.1.100", "998877");

        // Lookup by machine name
        bool foundByName = manager.TryGetPassword("DESKTOP-OFFICE", null, out string passByName);
        Assert.True(foundByName);
        Assert.Equal("998877", passByName);

        // Lookup by IP address
        bool foundByIp = manager.TryGetPassword(null, "192.168.1.100", out string passByIp);
        Assert.True(foundByIp);
        Assert.Equal("998877", passByIp);

        // Case insensitivity check
        bool foundLower = manager.TryGetPassword("desktop-office", null, out string passLower);
        Assert.True(foundLower);
        Assert.Equal("998877", passLower);
    }

    [Fact]
    public void SettingsManager_RemovePassword_ClearsCredential()
    {
        var manager = new SettingsManager(_tempSettingsPath);
        manager.SavePassword("LAPTOP-01", "10.0.0.50", "123456");

        Assert.True(manager.HasSavedPassword("LAPTOP-01", "10.0.0.50"));

        manager.RemovePassword("LAPTOP-01", "10.0.0.50");

        Assert.False(manager.HasSavedPassword("LAPTOP-01", "10.0.0.50"));
        Assert.False(manager.TryGetPassword("LAPTOP-01", null, out _));
    }

    [Fact]
    public void SettingsManager_UnattendedAccess_Persists_And_Loads()
    {
        var manager1 = new SettingsManager(_tempSettingsPath);
        Assert.False(manager1.IsUnattendedAccessEnabled());
        Assert.Null(manager1.GetUnattendedPassword());

        manager1.SetUnattendedAccess(true, "PermanentHostPass99!");
        Assert.True(manager1.IsUnattendedAccessEnabled());
        Assert.Equal("PermanentHostPass99!", manager1.GetUnattendedPassword());

        // Reload from fresh instance
        var manager2 = new SettingsManager(_tempSettingsPath);
        Assert.True(manager2.IsUnattendedAccessEnabled());
        Assert.Equal("PermanentHostPass99!", manager2.GetUnattendedPassword());

        // Disable unattended access
        manager2.SetUnattendedAccess(false);
        Assert.False(manager2.IsUnattendedAccessEnabled());
        // Password retained for convenience when re-enabling
        Assert.Equal("PermanentHostPass99!", manager2.GetUnattendedPassword());
    }

    [Fact]
    public void SettingsManager_UnauthorizedAccess_Defaults()
    {
        var manager = new SettingsManager(_tempSettingsPath);
        Assert.True(manager.BlockUnauthorizedAttempts);
        Assert.Equal(5, manager.MaxFailedAuthAttempts);
        Assert.Equal(10, manager.LockoutDurationMinutes);
        Assert.False(manager.StartMinimizedToTray);
        Assert.True(manager.MinimizeToTrayOnClose);
    }

    [Fact]
    public void SettingsManager_LockoutLogic_TriggersAfterMaxAttempts()
    {
        var manager = new SettingsManager(_tempSettingsPath)
        {
            BlockUnauthorizedAttempts = true,
            MaxFailedAuthAttempts = 3,
            LockoutDurationMinutes = 15
        };

        string testIp = "192.168.1.99";

        // First attempt - not locked out
        manager.RecordFailedAttempt(testIp);
        Assert.False(manager.IsIpLockedOut(testIp, out _));

        // Second attempt - not locked out
        manager.RecordFailedAttempt(testIp);
        Assert.False(manager.IsIpLockedOut(testIp, out _));

        // Third attempt - now locked out!
        manager.RecordFailedAttempt(testIp);
        bool locked = manager.IsIpLockedOut(testIp, out TimeSpan remaining);
        Assert.True(locked);
        Assert.True(remaining.TotalMinutes > 14);
        Assert.Equal(1, manager.GetActiveLockoutsCount());
    }

    [Fact]
    public void SettingsManager_ResetFailedAttempts_ClearsLockout()
    {
        var manager = new SettingsManager(_tempSettingsPath)
        {
            BlockUnauthorizedAttempts = true,
            MaxFailedAuthAttempts = 2
        };

        string testIp = "192.168.1.101";

        manager.RecordFailedAttempt(testIp);
        manager.RecordFailedAttempt(testIp);
        Assert.True(manager.IsIpLockedOut(testIp, out _));

        manager.ResetFailedAttempts(testIp);
        Assert.False(manager.IsIpLockedOut(testIp, out _));
    }

    [Fact]
    public void SettingsManager_ClearAllLockouts_RemovesAllBlocks()
    {
        var manager = new SettingsManager(_tempSettingsPath)
        {
            BlockUnauthorizedAttempts = true,
            MaxFailedAuthAttempts = 1
        };

        manager.RecordFailedAttempt("10.0.0.1");
        manager.RecordFailedAttempt("10.0.0.2");
        Assert.Equal(2, manager.GetActiveLockoutsCount());

        manager.ClearAllLockouts();
        Assert.Equal(0, manager.GetActiveLockoutsCount());
        Assert.False(manager.IsIpLockedOut("10.0.0.1", out _));
        Assert.False(manager.IsIpLockedOut("10.0.0.2", out _));
    }

    [Fact]
    public void SettingsManager_GeneralPreferences_Persist()
    {
        var manager1 = new SettingsManager(_tempSettingsPath)
        {
            StartMinimizedToTray = true,
            MinimizeToTrayOnClose = false,
            MaxFailedAuthAttempts = 10,
            LockoutDurationMinutes = 60,
            BlockUnauthorizedAttempts = false
        };

        var manager2 = new SettingsManager(_tempSettingsPath);
        Assert.True(manager2.StartMinimizedToTray);
        Assert.False(manager2.MinimizeToTrayOnClose);
        Assert.Equal(10, manager2.MaxFailedAuthAttempts);
        Assert.Equal(60, manager2.LockoutDurationMinutes);
        Assert.False(manager2.BlockUnauthorizedAttempts);
    }
}
