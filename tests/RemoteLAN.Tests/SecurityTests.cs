using RemoteLAN.Agent.Security;

namespace RemoteLAN.Tests;

public class SecurityTests
{
    [Fact]
    public void PinManager_Generates_Valid_SixDigitPin()
    {
        var pinMgr = new PinManager();
        string pin = pinMgr.CurrentPin;

        Assert.NotNull(pin);
        Assert.Equal(6, pin.Length);
        Assert.True(int.TryParse(pin, out int value));
        Assert.InRange(value, 100000, 999999);
    }

    [Fact]
    public void PinManager_Validates_CorrectPin_And_Rejects_IncorrectPin()
    {
        var pinMgr = new PinManager("554433");

        Assert.True(pinMgr.ValidatePin("554433"));
        Assert.True(pinMgr.ValidatePin(" 554433 ")); // whitespace trimmed

        Assert.False(pinMgr.ValidatePin("554432"));
        Assert.False(pinMgr.ValidatePin(""));
        Assert.False(pinMgr.ValidatePin("   "));
        Assert.False(pinMgr.ValidatePin(null!));
    }

    [Fact]
    public void PinManager_RegeneratePin_ChangesValue()
    {
        var pinMgr = new PinManager("111111");
        bool eventFired = false;
        pinMgr.PinChanged += _ => eventFired = true;

        pinMgr.RegeneratePin();

        Assert.NotEqual("111111", pinMgr.CurrentPin);
        Assert.True(eventFired);
    }
}
