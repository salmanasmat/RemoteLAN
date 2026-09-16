using System.Windows;
using RemoteLAN.Input;

namespace RemoteLAN.Tests;

public class CoordinateMappingTests
{
    [Fact]
    public void Translate_ExactMatch_CenterReturnsHalf()
    {
        // Viewport is 1920x1080, remote is 1920x1080
        var click = new Point(960, 540);
        var (inBounds, normX, normY) = CoordinateTranslator.TranslateToNormalized(
            click,
            elementActualWidth: 1920,
            elementActualHeight: 1080,
            remoteScreenWidth: 1920,
            remoteScreenHeight: 1080);

        Assert.True(inBounds);
        Assert.Equal(0.5, normX, 4);
        Assert.Equal(0.5, normY, 4);
    }

    [Fact]
    public void Translate_Pillarbox_CentersHorizontally()
    {
        // Container is 2000x1000 (aspect 2.0). Remote is 1000x1000 (aspect 1.0).
        // Displayed size is 1000x1000, centered in 2000 width => horizontal offset = 500.
        // Click at X=500, Y=0 should be (0.0, 0.0)
        var click = new Point(500, 0);
        var (inBounds, normX, normY) = CoordinateTranslator.TranslateToNormalized(
            click,
            elementActualWidth: 2000,
            elementActualHeight: 1000,
            remoteScreenWidth: 1000,
            remoteScreenHeight: 1000);

        Assert.True(inBounds);
        Assert.Equal(0.0, normX, 4);
        Assert.Equal(0.0, normY, 4);

        // Click at X=100 (in the left pillarbox bar) should be Out of Bounds
        var clickOutside = new Point(100, 500);
        var (outsideBounds, _, _) = CoordinateTranslator.TranslateToNormalized(
            clickOutside,
            elementActualWidth: 2000,
            elementActualHeight: 1000,
            remoteScreenWidth: 1000,
            remoteScreenHeight: 1000);

        Assert.False(outsideBounds);
    }

    [Fact]
    public void Translate_Letterbox_CentersVertically()
    {
        // Container is 1000x2000 (aspect 0.5). Remote is 1000x1000 (aspect 1.0).
        // Displayed size is 1000x1000, centered in 2000 height => vertical offset = 500.
        // Click at X=500, Y=1000 should be center (0.5, 0.5)
        var click = new Point(500, 1000);
        var (inBounds, normX, normY) = CoordinateTranslator.TranslateToNormalized(
            click,
            elementActualWidth: 1000,
            elementActualHeight: 2000,
            remoteScreenWidth: 1000,
            remoteScreenHeight: 1000);

        Assert.True(inBounds);
        Assert.Equal(0.5, normX, 4);
        Assert.Equal(0.5, normY, 4);

        // Click at X=500, Y=200 (in the top letterbox bar) should be Out of Bounds
        var clickOutside = new Point(500, 200);
        var (outsideBounds, _, _) = CoordinateTranslator.TranslateToNormalized(
            clickOutside,
            elementActualWidth: 1000,
            elementActualHeight: 2000,
            remoteScreenWidth: 1000,
            remoteScreenHeight: 1000);

        Assert.False(outsideBounds);
    }

    [Fact]
    public void Translate_InvalidDimensions_ReturnsFalse()
    {
        var (inBounds, _, _) = CoordinateTranslator.TranslateToNormalized(
            new Point(50, 50),
            elementActualWidth: 0,
            elementActualHeight: 0,
            remoteScreenWidth: 1920,
            remoteScreenHeight: 1080);

        Assert.False(inBounds);
    }
}
