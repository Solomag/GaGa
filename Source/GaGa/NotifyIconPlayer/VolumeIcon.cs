// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;


namespace GaGa.NotifyIconPlayer
{
    /// <summary>
    /// A tray icon with the volume percent written on it,
    /// shown for a moment while the volume changes.
    /// </summary>
    internal static class VolumeIcon
    {
        [DllImport("user32.dll")]
        private static extern Boolean DestroyIcon(IntPtr handle);

        /// <summary>
        /// Draw the icon. The caller disposes it with Free().
        /// </summary>
        public static Icon Render(Int32 percent, Boolean muted)
        {
            Size size = SystemInformation.SmallIconSize;
            Int32 s = Math.Max(16, size.Width);

            using (Bitmap bitmap = new Bitmap(s, s))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                // a rounded badge, red when muted:
                Color back = muted ? Color.FromArgb(200, 50, 50) : Color.FromArgb(40, 40, 46);
                using (GraphicsPath badge = RoundedRect(new RectangleF(0, 0, s - 1, s - 1), s / 4f))
                using (SolidBrush brush = new SolidBrush(back))
                {
                    g.FillPath(brush, badge);
                }

                // the number, as big as fits ("100" is the widest):
                String text = muted ? "M" : percent.ToString();

                // the digits as outlines, stretched to fill the badge
                // (squeezed sideways when they are too wide, like "100"):
                using (GraphicsPath digits = new GraphicsPath())
                {
                    digits.AddString(text, new FontFamily("Segoe UI"), (Int32) FontStyle.Bold, s,
                                     PointF.Empty, StringFormat.GenericTypographic);

                    RectangleF bounds = digits.GetBounds();
                    Single height = s * 0.72f;
                    Single width = s - 3f;

                    Single sy = height / bounds.Height;
                    Single sx = Math.Min(sy, width / bounds.Width);

                    using (Matrix fit = new Matrix())
                    {
                        fit.Translate(s / 2f, s / 2f);
                        fit.Scale(sx, sy);
                        fit.Translate(-(bounds.X + bounds.Width / 2f), -(bounds.Y + bounds.Height / 2f));
                        digits.Transform(fit);
                    }

                    g.FillPath(Brushes.White, digits);
                }

                return Icon.FromHandle(bitmap.GetHicon());
            }
        }

        /// <summary>
        /// Release an icon made by Render().
        /// </summary>
        public static void Free(Icon icon)
        {
            if (icon == null)
                return;

            IntPtr handle = icon.Handle;
            icon.Dispose();
            DestroyIcon(handle);
        }

        private static GraphicsPath RoundedRect(RectangleF r, Single radius)
        {
            Single d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
