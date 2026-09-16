using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace RemoteLAN.Capture;

public static class PlaceholderFrameHelper
{
    public static Bitmap RenderLockPlaceholder(
        ref Bitmap? targetBitmap,
        ref Graphics? targetGraphics,
        int width,
        int height,
        string engineName,
        string? customReason = null)
    {
        int w = width > 0 ? width : 1920;
        int h = height > 0 ? height : 1080;

        if (targetBitmap == null || targetGraphics == null || targetBitmap.Width != w || targetBitmap.Height != h)
        {
            try
            {
                targetGraphics?.Dispose();
                targetBitmap?.Dispose();
            }
            catch { }

            targetBitmap = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            targetGraphics = Graphics.FromImage(targetBitmap);
        }

        var g = targetGraphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Background: Deep modern dark slate (#090D16)
        using (var bgBrush = new SolidBrush(Color.FromArgb(9, 13, 22)))
        {
            g.FillRectangle(bgBrush, 0, 0, w, h);
        }

        // Center card dimensions
        int cardWidth = Math.Min(680, Math.Max(320, w - 60));
        int cardHeight = Math.Min(340, Math.Max(220, h - 60));
        int cardX = (w - cardWidth) / 2;
        int cardY = (h - cardHeight) / 2;

        if (cardWidth > 150 && cardHeight > 150)
        {
            using var cardBrush = new SolidBrush(Color.FromArgb(15, 23, 42)); // #0F172A
            using var cardPen = new Pen(Color.FromArgb(51, 65, 85), 1.5f); // #334155
            var cardRect = new Rectangle(cardX, cardY, cardWidth, cardHeight);
            g.FillRectangle(cardBrush, cardRect);
            g.DrawRectangle(cardPen, cardRect);

            // Top decorative accent line (Amber #F59E0B)
            using var accentBrush = new SolidBrush(Color.FromArgb(245, 158, 11));
            g.FillRectangle(accentBrush, cardX, cardY, cardWidth, 4);

            // Badge: "LOCK SCREEN ACTIVE"
            int badgeX = cardX + 32;
            int badgeY = cardY + 26;
            using var badgeBgBrush = new SolidBrush(Color.FromArgb(69, 26, 3)); // dark amber
            using var badgeBorderPen = new Pen(Color.FromArgb(217, 119, 6), 1);
            var badgeRect = new Rectangle(badgeX, badgeY, 175, 26);
            g.FillRectangle(badgeBgBrush, badgeRect);
            g.DrawRectangle(badgeBorderPen, badgeRect);

            using var badgeFont = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold);
            using var badgeTextBrush = new SolidBrush(Color.FromArgb(251, 191, 36)); // #FBBF24
            g.DrawString("LOCK SCREEN ACTIVE", badgeFont, badgeTextBrush, badgeX + 12, badgeY + 6);

            // Main Title
            using var titleFont = new Font(FontFamily.GenericSansSerif, 18, FontStyle.Bold);
            using var titleBrush = new SolidBrush(Color.FromArgb(248, 250, 252)); // #F8FAFC
            g.DrawString("Remote Desktop Session Locked", titleFont, titleBrush, cardX + 30, cardY + 68);

            // Explanation lines
            using var bodyFont = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Regular);
            using var bodyBrush = new SolidBrush(Color.FromArgb(203, 213, 225)); // #CBD5E1
            using var tipBrush = new SolidBrush(Color.FromArgb(148, 163, 184)); // #94A3B8

            string line1 = "The remote Windows desktop is currently locked at the sign-in screen.";
            string line2 = "Windows isolates the secure logon desktop from user-mode remote capture.";
            string line3 = "Live desktop streaming will resume automatically when the PC is unlocked.";

            g.DrawString(line1, bodyFont, bodyBrush, cardX + 32, cardY + 112);
            g.DrawString(line2, bodyFont, tipBrush, cardX + 32, cardY + 140);
            g.DrawString(line3, bodyFont, bodyBrush, cardX + 32, cardY + 172);

            // Status box at bottom of card
            int statusY = cardY + cardHeight - 64;
            using var statusBgBrush = new SolidBrush(Color.FromArgb(30, 41, 59)); // #1E293B
            g.FillRectangle(statusBgBrush, cardX + 24, statusY, cardWidth - 48, 42);

            using var statusFont = new Font(FontFamily.GenericSansSerif, 9.5f, FontStyle.Regular);
            using var statusDotBrush = new SolidBrush(Color.FromArgb(245, 158, 11)); // Amber dot
            g.FillEllipse(statusDotBrush, cardX + 38, statusY + 16, 10, 10);

            using var statusTextBrush = new SolidBrush(Color.FromArgb(226, 232, 240));
            string statusStr = $"Waiting for unlock • Resolution: {w}x{h} • Time: {DateTime.Now:HH:mm:ss} • Engine: {engineName}";
            g.DrawString(statusStr, statusFont, statusTextBrush, cardX + 56, statusY + 13);
        }

        return targetBitmap;
    }
}
