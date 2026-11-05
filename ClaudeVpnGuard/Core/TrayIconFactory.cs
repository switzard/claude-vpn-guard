using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ClaudeVpnGuard.Core;

/// <summary>
/// Flat unslop tray mark: solid shield only (no lock — locks look like a face at 16px).
/// </summary>
public static class TrayIconFactory
{
    private static readonly object Gate = new();
    private static readonly Dictionary<int, Icon> Cache = new();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Create(Color accent)
    {
        var key = accent.ToArgb();
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
                return (Icon)cached.Clone();

            using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                using var shieldPath = BuildShieldPath(16, 3, 26, 28);
                using var fill = new SolidBrush(accent);
                g.FillPath(fill, shieldPath);

                // Tiny check notch — reads at 16px, never a "face"
                using var ink = new Pen(Color.FromArgb(255, 14, 17, 20), 2.4f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                g.DrawLines(ink, new[]
                {
                    new PointF(11.5f, 16f),
                    new PointF(14.5f, 19f),
                    new PointF(20.5f, 12.5f)
                });
            }

            var hIcon = bmp.GetHicon();
            try
            {
                using var temp = Icon.FromHandle(hIcon);
                var owned = (Icon)temp.Clone();
                Cache[key] = owned;
                return (Icon)owned.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
    }

    private static GraphicsPath BuildShieldPath(float cx, float top, float width, float bottom)
    {
        var left = cx - width / 2f;
        var right = cx + width / 2f;
        var midY = top + (bottom - top) * 0.42f;
        var path = new GraphicsPath();
        path.AddBezier(cx, top, right - 1, top + 2, right, midY - 2, right, midY);
        path.AddBezier(right, midY, right - 1, bottom - 6, cx + 4, bottom - 1, cx, bottom);
        path.AddBezier(cx, bottom, cx - 4, bottom - 1, left + 1, bottom - 6, left, midY);
        path.AddBezier(left, midY, left, midY - 2, left + 1, top + 2, cx, top);
        path.CloseFigure();
        return path;
    }

    public static Color ColorFor(bool guardEnabled, bool allowed, bool vpnUp) =>
        !guardEnabled ? Color.FromArgb(255, 160, 160, 160) :
        allowed ? Color.FromArgb(255, 46, 196, 182) :
        vpnUp ? Color.FromArgb(255, 240, 180, 41) :
        Color.FromArgb(255, 255, 90, 95);
}
