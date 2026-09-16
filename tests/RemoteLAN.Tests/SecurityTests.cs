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
}
