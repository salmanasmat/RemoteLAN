using System.Windows;

namespace RemoteLAN.Input;

public static class CoordinateTranslator
{
    public static (bool InBounds, double NormalizedX, double NormalizedY) TranslateToNormalized(
        Point mousePosition,
        double elementActualWidth,
        double elementActualHeight,
        int remoteScreenWidth,
        int remoteScreenHeight)
    {
        if (elementActualWidth <= 0 || elementActualHeight <= 0 || remoteScreenWidth <= 0 || remoteScreenHeight <= 0)
        {
            return (false, 0, 0);
        }

        // Account for Stretch="Uniform" letterbox / pillarbox
        double scaleX = elementActualWidth / remoteScreenWidth;
        double scaleY = elementActualHeight / remoteScreenHeight;
        double scale = Math.Min(scaleX, scaleY);

        double displayedWidth = remoteScreenWidth * scale;
        double displayedHeight = remoteScreenHeight * scale;

        double offsetX = (elementActualWidth - displayedWidth) / 2.0;
        double offsetY = (elementActualHeight - displayedHeight) / 2.0;

        double relX = mousePosition.X - offsetX;
        double relY = mousePosition.Y - offsetY;

        if (relX < 0 || relX > displayedWidth || relY < 0 || relY > displayedHeight)
        {
            return (false, 0, 0);
        }

        double normalizedX = Math.Clamp(relX / displayedWidth, 0.0, 1.0);
        double normalizedY = Math.Clamp(relY / displayedHeight, 0.0, 1.0);

        return (true, normalizedX, normalizedY);
    }
}
