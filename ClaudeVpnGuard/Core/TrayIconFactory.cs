using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ClaudeVpnGuard.Core;

public static class TrayIconFactory
{
    public static Icon Create(Color accent)
    {
        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(255, 14, 17, 20));
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var ring = new Pen(accent, 3f);
            g.DrawEllipse(ring, 4, 4, 24, 24);
            using var core = new SolidBrush(accent);
            g.FillEllipse(core, 11, 11, 10, 10);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    public static Color ColorFor(bool guardEnabled, bool allowed, bool vpnUp) =>
        !guardEnabled ? Color.FromArgb(255, 160, 160, 160) :
        allowed ? Color.FromArgb(255, 46, 196, 182) :
        vpnUp ? Color.FromArgb(255, 240, 180, 41) :
        Color.FromArgb(255, 255, 90, 95);
}
