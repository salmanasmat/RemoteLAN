using System.Drawing;
using System.Windows;
using RemoteLAN.Capture;
using RemoteLAN.Input;
using RemoteLAN.Network;
using RemoteLAN.Power;
using Point = System.Windows.Point;

namespace RemoteLAN.Tests;

public class LockAndResolutionRecoveryTests
{
    [Fact]
    public void PlaceholderFrameHelper_RendersValidBitmap()
    {
        Bitmap? bmp = null;
        Graphics? g = null;

        var result = PlaceholderFrameHelper.RenderLockPlaceholder(
            ref bmp,
            ref g,
            1920,
            1080,
            "DXGI Desktop Duplication");

        Assert.NotNull(result);
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
        Assert.NotNull(bmp);
        Assert.NotNull(g);

        // Verify it adapts dynamically if resolution changes (e.g. 1024x768 during lock)
        var result2 = PlaceholderFrameHelper.RenderLockPlaceholder(
            ref bmp,
            ref g,
            1024,
            768,
            "GDI BitBlt");

        Assert.NotNull(result2);
        Assert.Equal(1024, result2.Width);
        Assert.Equal(768, result2.Height);

        g?.Dispose();
        bmp?.Dispose();
    }

    [Fact]
    public void ControllerClient_UpdateRemoteResolution_TriggersEventAndUpdatesProperties()
    {
        using var client = new ControllerClient();
        int eventWidth = 0;
        int eventHeight = 0;
        int eventCount = 0;

        client.ScreenResolutionReceived += (w, h) =>
        {
            eventWidth = w;
            eventHeight = h;
            eventCount++;
        };

        // Initial update
        client.UpdateRemoteResolution(1024, 768);
        Assert.Equal(1024, client.RemoteScreenWidth);
        Assert.Equal(768, client.RemoteScreenHeight);
        Assert.Equal(1024, eventWidth);
        Assert.Equal(768, eventHeight);
        Assert.Equal(1, eventCount);

        // Same resolution should be ignored (no duplicate event)
        client.UpdateRemoteResolution(1024, 768);
        Assert.Equal(1, eventCount);

        // Dynamic resolution change upon desktop unlock to native 1920x1080
        client.UpdateRemoteResolution(1920, 1080);
        Assert.Equal(1920, client.RemoteScreenWidth);
        Assert.Equal(1080, client.RemoteScreenHeight);
        Assert.Equal(1920, eventWidth);
        Assert.Equal(1080, eventHeight);
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public void SystemPowerManager_AcquireAndRelease_DoesNotThrow()
    {
        // Verify keep-awake acquire and release execute safely and handle reference counting
        SystemPowerManager.AcquireKeepAwake();
        SystemPowerManager.AcquireKeepAwake();
        SystemPowerManager.WakeDisplay();
        SystemPowerManager.ReleaseKeepAwake();
        SystemPowerManager.ReleaseKeepAwake();
    }

    [Fact]
    public void GdiScreenCapturer_InitializeAndEngineName()
    {
        using var gdi = new GdiScreenCapturer();
        bool init = gdi.Initialize();
        Assert.True(init);
        Assert.Equal("GDI BitBlt", gdi.EngineName);
        Assert.True(gdi.Width > 0);
        Assert.True(gdi.Height > 0);
    }

    [Fact]
    public void DynamicResolutionAdaptation_UpdatesCoordinateMappingAccurately()
    {
        // Simulate client receiving 1024x768 while remote was locked
        using var client = new ControllerClient();
        client.UpdateRemoteResolution(1024, 768);

        var click = new Point(512, 384);
        var (inBounds1, normX1, normY1) = CoordinateTranslator.TranslateToNormalized(
            click,
            elementActualWidth: 1024,
            elementActualHeight: 768,
            client.RemoteScreenWidth,
            client.RemoteScreenHeight);

        Assert.True(inBounds1);
        Assert.Equal(0.5, normX1, 3);
        Assert.Equal(0.5, normY1, 3);

        // Remote workstation unlocks and resolution expands to 1920x1080
        client.UpdateRemoteResolution(1920, 1080);

        var click2 = new Point(960, 540);
        var (inBounds2, normX2, normY2) = CoordinateTranslator.TranslateToNormalized(
            click2,
            elementActualWidth: 1920,
            elementActualHeight: 1080,
            client.RemoteScreenWidth,
            client.RemoteScreenHeight);

        Assert.True(inBounds2);
        Assert.Equal(0.5, normX2, 3);
        Assert.Equal(0.5, normY2, 3);
    }
}
