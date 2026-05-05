using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ItimHebrewCalendar.Services;

namespace ItimHebrewCalendar.Windows
{
    // Renders the day glyph as a tray HICON in one of two styles:
    //   Tile     - flat solid rounded square, white text (Win11-tile look).
    //   TextOnly - transparent background, foreground-color text only, like the
    //              built-in clock/Wi-Fi tray icons.
    // After sunset is signalled with orange in both styles.
    // Caller must release the HICON via DestroyIcon.
    public static class TrayIconRenderer
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public static IntPtr RenderHIcon(
            string text,
            bool darkMode,
            bool afterSunset = false,
            TrayIconStyle style = TrayIconStyle.Tile,
            int size = 32)
        {
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.Transparent);

                if (style == TrayIconStyle.Tile)
                {
                    var fillColor = afterSunset
                        ? Color.FromArgb(255, 0xE0, 0x55, 0x10)
                        : (darkMode
                            ? Color.FromArgb(255, 0x4C, 0xC2, 0xFF)
                            : Color.FromArgb(255, 0x00, 0x67, 0xC0));

                    float padding = 0.5f;
                    float cornerR = size * 0.22f;
                    var frameRect = new RectangleF(padding, padding, size - padding * 2, size - padding * 2);

                    using (var bodyPath = CreateRoundedRect(frameRect, cornerR))
                    using (var bodyBrush = new SolidBrush(fillColor))
                        g.FillPath(bodyBrush, bodyPath);

                    using (var borderPath = CreateRoundedRect(frameRect, cornerR))
                    using (var borderPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
                        g.DrawPath(borderPen, borderPath);

                    DrawGlyphMaxFill(g, text, FontStyle.Bold, Color.White, size, inset: 1.5f);
                }
                else
                {
                    // Match Windows tray foreground: white in dark mode, near-black
                    // in light mode. Regular weight matches the native clock/Wi-Fi
                    // label glyphs; sunset gets a theme-tuned orange instead.
                    var textColor = afterSunset
                        ? (darkMode
                            ? Color.FromArgb(255, 0xFF, 0xB0, 0x60)
                            : Color.FromArgb(255, 0xC0, 0x45, 0x00))
                        : (darkMode
                            ? Color.White
                            : Color.FromArgb(255, 0x20, 0x20, 0x20));

                    DrawGlyphMaxFill(g, text, FontStyle.Regular, textColor, size, inset: 0.5f);
                }
            }

            return bmp.GetHicon();
        }

        public static Icon Render(
            string text,
            bool darkMode,
            bool afterSunset = false,
            TrayIconStyle style = TrayIconStyle.Tile,
            int size = 32)
        {
            var hIcon = RenderHIcon(text, darkMode, afterSunset, style, size);
            return Icon.FromHandle(hIcon);
        }

        // Renders text at the largest size whose ink bounds still fit within
        // (size - inset*2) on both axes, then centers those ink bounds in the
        // icon. Using GraphicsPath bounds (instead of MeasureString) ignores
        // line-leading and per-side typographic padding, so narrow Hebrew
        // glyphs like "י" or "ל" can fully fill the icon vertically.
        private static void DrawGlyphMaxFill(
            Graphics g, string text, FontStyle fontStyle, Color color, int size, float inset)
        {
            if (string.IsNullOrEmpty(text)) return;

            float availW = Math.Max(1f, size - inset * 2f);
            float availH = Math.Max(1f, size - inset * 2f);

            using var family = new FontFamily("Segoe UI");
            using var sf = new StringFormat
            {
                FormatFlags = StringFormatFlags.DirectionRightToLeft
            };

            const float refSize = 100f;
            using var probe = new GraphicsPath();
            probe.AddString(text, family, (int)fontStyle, refSize, new PointF(0, 0), sf);
            var refBounds = probe.GetBounds();
            if (refBounds.Width <= 0 || refBounds.Height <= 0) return;

            float scale = Math.Min(availW / refBounds.Width, availH / refBounds.Height);
            float finalSize = refSize * scale;

            using var path = new GraphicsPath();
            path.AddString(text, family, (int)fontStyle, finalSize, new PointF(0, 0), sf);
            var b = path.GetBounds();

            using var matrix = new Matrix();
            matrix.Translate((size - b.Width) / 2f - b.X, (size - b.Height) / 2f - b.Y);
            path.Transform(matrix);

            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }

        private static GraphicsPath CreateRoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
