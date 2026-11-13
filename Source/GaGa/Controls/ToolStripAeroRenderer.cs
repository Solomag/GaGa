
// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Drawing;
using System.Windows.Forms;


namespace GaGa.Controls
{
    internal class AeroColorTable : ProfessionalColorTable
    {
        private Color lastAeroColor;
        private Color menuItemSelected;
        private Color menuItemBorder;

        /// <summary>
        /// A color table that tries to match the current aero theme
        /// when rendering selected menu items.
        /// </summary>
        public AeroColorTable()
        {
            lastAeroColor = Color.Empty;
            menuItemSelected = base.MenuItemSelected;
            menuItemBorder = base.MenuItemBorder;
        }

        /// <summary>
        /// Update the colors to match the current aero theme.
        /// </summary>
        public void UpdateColors()
        {
            Color aeroColor = Util.GetCurrentAeroColor();

            // unable to read it, fallback to default colors:
            if (aeroColor == Color.Empty)
            {
                menuItemSelected = base.MenuItemSelected;
                menuItemBorder = base.MenuItemBorder;
            }
            else
            {
                // recalculate when needed:
                if (aeroColor != lastAeroColor)
                {
                    lastAeroColor = aeroColor;
                    RecalculateColors(aeroColor);
                }
            }
        }

        /// <summary>
        /// Recalculate our color values from a given base color.
        /// </summary>
        private void RecalculateColors(Color baseColor)
        {
            Double A = (Double) baseColor.A;
            Double R = (Double) baseColor.R;
            Double G = (Double) baseColor.G;
            Double B = (Double) baseColor.B;

            // too low alpha for clear visibility, darken it:
            if (A < 30)
            {
                A = 30;
            }

            // we want an opaque color, so remove alpha
            // but keep the current color value:
            // c = c * (alpha / 255) + (255 * (1 - (alpha / 255)))
            R = R * (A / 255) + (255 * (1 - (A / 255)));
            G = G * (A / 255) + (255 * (1 - (A / 255)));
            B = B * (A / 255) + (255 * (1 - (A / 255)));

            // we don't want colors too close to white
            // those would be indistinguishable from the background:
            if ((R > 220) && (G > 220) && (B > 220))
            {
                R = G = B = 220;
            }

            // we don't want color too close to black
            // those would obscure the text:
            if ((R < 50) && (G < 50) && (B < 50))
            {
                R = G = B = 50;
            }

            R = Util.Clamp(R, 0, 255);
            G = Util.Clamp(G, 0, 255);
            B = Util.Clamp(B, 0, 255);

            Color color = Color.FromArgb((Int32) R, (Int32) G, (Int32) B);

            // right now, both the selected menu and the border use
            // the same color but we could also darken the border a bit:
            menuItemSelected = color;
            menuItemBorder = color;
        }

        /// <summary>
        /// Gets the solid color to use when a ToolStripMenuItem is selected.
        /// </summary>
        public override Color MenuItemSelected
        {
            get
            {
                return Theme.IsDark ? DarkSelected(menuItemSelected) : menuItemSelected;
            }
        }

        /// <summary>
        /// The accent color, toned down to sit on a dark background.
        /// </summary>
        private static Color DarkSelected(Color accent)
        {
            Color back = Theme.DarkControl;
            return Color.FromArgb(
                (accent.R * 35 + back.R * 65) / 100,
                (accent.G * 35 + back.G * 65) / 100,
                (accent.B * 35 + back.B * 65) / 100);
        }

        ///
        /// Dark theme: everything else
        ///

        public override Color ToolStripDropDownBackground
        {
            get { return Theme.IsDark ? Theme.DarkBack : base.ToolStripDropDownBackground; }
        }

        public override Color ImageMarginGradientBegin
        {
            get { return Theme.IsDark ? Theme.DarkBack : base.ImageMarginGradientBegin; }
        }

        public override Color ImageMarginGradientMiddle
        {
            get { return Theme.IsDark ? Theme.DarkBack : base.ImageMarginGradientMiddle; }
        }

        public override Color ImageMarginGradientEnd
        {
            get { return Theme.IsDark ? Theme.DarkBack : base.ImageMarginGradientEnd; }
        }

        public override Color MenuBorder
        {
            get { return Theme.IsDark ? Theme.DarkBorder : base.MenuBorder; }
        }

        public override Color SeparatorDark
        {
            get { return Theme.IsDark ? Theme.DarkBorder : base.SeparatorDark; }
        }

        public override Color SeparatorLight
        {
            get { return Theme.IsDark ? Theme.DarkBack : base.SeparatorLight; }
        }

        public override Color MenuItemPressedGradientBegin
        {
            get { return Theme.IsDark ? Theme.DarkControl : base.MenuItemPressedGradientBegin; }
        }

        public override Color MenuItemPressedGradientEnd
        {
            get { return Theme.IsDark ? Theme.DarkControl : base.MenuItemPressedGradientEnd; }
        }

        public override Color CheckBackground
        {
            get { return Theme.IsDark ? DarkSelected(menuItemSelected) : base.CheckBackground; }
        }

        public override Color CheckSelectedBackground
        {
            get { return Theme.IsDark ? DarkSelected(menuItemSelected) : base.CheckSelectedBackground; }
        }

        public override Color CheckPressedBackground
        {
            get { return Theme.IsDark ? DarkSelected(menuItemSelected) : base.CheckPressedBackground; }
        }

        /// <summary>
        /// Gets the border color to use with ToolStripMenuItem.
        /// </summary>
        public override Color MenuItemBorder
        {
            get
            {
                return menuItemBorder;
            }
        }
    }

    internal class ToolStripAeroRenderer : ToolStripProfessionalRenderer
    {
        /// <summary>
        /// A renderer that tries to match the current aero theme
        /// when drawing selected menu items.
        /// </summary>
        public ToolStripAeroRenderer() : base(new AeroColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (Theme.IsDark)
                e.TextColor = e.Item.Enabled ? Theme.DarkText : Theme.DarkDisabledText;

            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            if (Theme.IsDark)
                e.ArrowColor = e.Item.Enabled ? Theme.DarkText : Theme.DarkDisabledText;

            base.OnRenderArrow(e);
        }

        /// <summary>
        /// The stock check mark is a black image, invisible on dark menus:
        /// draw our own there.
        /// </summary>
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            if (!Theme.IsDark)
            {
                base.OnRenderItemCheck(e);
                return;
            }

            Rectangle r = e.ImageRectangle;
            if (r.Width <= 0 || r.Height <= 0)
                return;

            using (SolidBrush back = new SolidBrush(ColorTable.CheckBackground))
                e.Graphics.FillRectangle(back, r);

            System.Drawing.Drawing2D.SmoothingMode previous = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using (Pen pen = new Pen(Theme.DarkText, Math.Max(1.6f, r.Width / 9f)))
            {
                e.Graphics.DrawLines(pen, new PointF[]
                {
                    new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                    new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f),
                });
            }

            e.Graphics.SmoothingMode = previous;
        }

        /// <summary>
        /// Update the colors to match the current aero theme.
        /// </summary>
        public void UpdateColors()
        {
            ((AeroColorTable) ColorTable).UpdateColors();
        }
    }
}

