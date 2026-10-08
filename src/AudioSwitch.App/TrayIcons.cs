using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AudioSwitch.App;

/// <summary>Draws the tray icon at the current DPI: white headphones on a rounded square (same design as app.ico,
/// see tools/make-icon.ps1), orange when working, grey when GG/Sonar is not reachable.</summary>
static class TrayIcons
{
    public static readonly Color Active = Color.FromArgb(232, 89, 12);
    public static readonly Color Inactive = Color.FromArgb(120, 120, 120);
    const string GlyphFont = "Segoe MDL2 Assets";
    const char HeadphoneGlyph = '';

    public static Icon Create(Color background)
    {
        var size = SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var path = RoundedSquare(size, Math.Max(2, (int)(size * 0.22))))
            using (var brush = new SolidBrush(background))
                g.FillPath(brush, path);

            using var font = new Font(GlyphFont, size * 0.7f, GraphicsUnit.Pixel);
            if (font.Name == GlyphFont) // the font silently falls back when missing; a plain square beats a "tofu" box
            {
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(HeadphoneGlyph.ToString(), font, Brushes.White, new RectangleF(0, size * 0.04f, size, size), format);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    static GraphicsPath RoundedSquare(int size, int radius)
    {
        var d = radius * 2;
        var w = size - 1;
        var path = new GraphicsPath();
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(w - d, 0, d, d, 270, 90);
        path.AddArc(w - d, w - d, d, d, 0, 90);
        path.AddArc(0, w - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);
}
