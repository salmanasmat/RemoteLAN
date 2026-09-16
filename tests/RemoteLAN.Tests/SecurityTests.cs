using RemoteLAN.Security;

namespace RemoteLAN.Tests;

public class SecurityTests
{
    [Fact]
    public void PinManager_Generates_Valid_AlphanumericCode()
    {
        var pinMgr = new PinManager();
        string pin = pinMgr.CurrentPin;

        Assert.NotNull(pin);
        Assert.Equal(6, pin.Length);
        Assert.All(pin, c => Assert.True(char.IsLetterOrDigit(c)));
    }

    [Fact]
    public void PinManager_Validates_AlphanumericCode_And_Rejects_Incorrect()
    {
        var pinMgr = new PinManager("7K2M9X");

        // Exact match
        Assert.True(pinMgr.ValidatePin("7K2M9X"));
        // Whitespace trimmed
        Assert.True(pinMgr.ValidatePin(" 7K2M9X "));
        // Case-insensitive session code
        Assert.True(pinMgr.ValidatePin("7k2m9x"));

        // Incorrect codes
        Assert.False(pinMgr.ValidatePin("7K2M9Y"));
        Assert.False(pinMgr.ValidatePin(""));
        Assert.False(pinMgr.ValidatePin("   "));
        Assert.False(pinMgr.ValidatePin(null!));
    }

    [Fact]
    public void PinManager_UnattendedAccess_Validation()
    {
        var pinMgr = new PinManager("SESSION1", unattendedAccessEnabled: true, unattendedPassword: "OfficePassword2026!");

        // Session code works
        Assert.True(pinMgr.ValidatePin("SESSION1"));
        Assert.True(pinMgr.ValidatePin("session1"));

        // Unattended password works
        Assert.True(pinMgr.ValidatePin("OfficePassword2026!"));
        // Unattended password is case-sensitive
        Assert.False(pinMgr.ValidatePin("officepassword2026!"));

        // Disable unattended access -> unattended password is now rejected
        pinMgr.UnattendedAccessEnabled = false;
        Assert.False(pinMgr.ValidatePin("OfficePassword2026!"));
        // Session code still works
        Assert.True(pinMgr.ValidatePin("SESSION1"));
    }

    [Fact]
    public void PinManager_RegeneratePin_ChangesValue()
    {
        var pinMgr = new PinManager("AAAAAA");
        bool eventFired = false;
        pinMgr.PinChanged += _ => eventFired = true;

        pinMgr.RegeneratePin();

        Assert.NotEqual("AAAAAA", pinMgr.CurrentPin);
        Assert.True(eventFired);
    }

    [Fact]
    public void PinManager_RotationInterval_Configuration()
    {
        using var pinMgr = new PinManager();
        Assert.Equal(0, pinMgr.RotationIntervalMinutes);

        pinMgr.SetRotationInterval(15);
        Assert.Equal(15, pinMgr.RotationIntervalMinutes);

        pinMgr.SetRotationInterval(0);
        Assert.Equal(0, pinMgr.RotationIntervalMinutes);

        // Negative values are clamped to 0
        pinMgr.SetRotationInterval(-5);
        Assert.Equal(0, pinMgr.RotationIntervalMinutes);
    }

    [Fact]
    public void PinManager_AutoRotation_RegeneratesPin()
    {
        using var pinMgr = new PinManager("PIN001");
        string original = pinMgr.CurrentPin;
        Assert.Equal("PIN001", original);

        bool eventFired = false;
        pinMgr.PinChanged += _ => eventFired = true;

        pinMgr.OnRotationTimerElapsed(null);

        Assert.NotEqual(original, pinMgr.CurrentPin);
        Assert.True(eventFired);
    }

    [Fact]
    public void SettingsManager_PinRotationInterval_Persistence()
    {
        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}.json");
        try
        {
            var mgr1 = new SettingsManager(tempFile);
            Assert.Equal(0, mgr1.PinRotationIntervalMinutes);

            mgr1.PinRotationIntervalMinutes = 30;
            Assert.Equal(30, mgr1.PinRotationIntervalMinutes);

            // Reload from file to verify persistence
            var mgr2 = new SettingsManager(tempFile);
            Assert.Equal(30, mgr2.PinRotationIntervalMinutes);
        }
        finally
        {
            try { if (System.IO.File.Exists(tempFile)) System.IO.File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void NativeMethods_MapVirtualKey_GeneratesValidScanCodes()
    {
        // 0x20 = VK_SPACE -> Scan code 0x39 on standard PC keyboard
        uint spaceScan = Input.NativeMethods.MapVirtualKey(0x20, Input.NativeMethods.MAPVK_VK_TO_VSC);
        Assert.True(spaceScan > 0, "VK_SPACE should map to a valid non-zero scan code");

        // 0x0D = VK_RETURN -> Scan code 0x1C on standard PC keyboard
        uint returnScan = Input.NativeMethods.MapVirtualKey(0x0D, Input.NativeMethods.MAPVK_VK_TO_VSC);
        Assert.True(returnScan > 0, "VK_RETURN should map to a valid non-zero scan code");

        // 0x41 = 'A' -> Scan code 0x1E
        uint aScan = Input.NativeMethods.MapVirtualKey(0x41, Input.NativeMethods.MAPVK_VK_TO_VSC);
        Assert.True(aScan > 0, "'A' should map to a valid non-zero scan code");
    }

    [Fact]
    public void InputInjector_Lifecycle_InitializesAndDisposesCleanly()
    {
        using var injector = new Input.InputInjector();
        // Verifies session lifecycle and queue initialization without physical desktop interference
        injector.ResetSession(1);
        injector.ResetSession(0);
    }
}
