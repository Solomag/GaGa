// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using Microsoft.Win32;


namespace GaGa.Controls
{
    /// <summary>
    /// Light or dark, chosen by the user or following Windows.
    /// </summary>
    internal enum ThemeMode
    {
        System = 0,
        Light = 1,
        Dark = 2,
    }

    /// <summary>
    /// Colors for the current theme, and helpers to apply them.
    /// </summary>
    internal static class Theme
    {
        public static ThemeMode Mode = ThemeMode.System;

        /// <summary>
        /// Whether Windows uses a dark taskbar (where the tray icon lives).
        /// Windows 10 1903+; older versions are always light.
        /// </summary>
        public static Boolean SystemIsDark
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        Object value = (key != null) ? key.GetValue("SystemUsesLightTheme") : null;
                        return (value is Int32) && (Int32) value == 0;
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static Boolean IsDark
        {
            get
            {
                switch (Mode)
                {
                    case ThemeMode.Dark: return true;
                    case ThemeMode.Light: return false;
                    default: return SystemIsDark;
                }
            }
        }

        // dark palette, close to the Windows 11 dark context menus:
        public static readonly Color DarkBack = Color.FromArgb(43, 43, 43);
        public static readonly Color DarkControl = Color.FromArgb(56, 56, 56);
        public static readonly Color DarkBorder = Color.FromArgb(70, 70, 70);
        public static readonly Color DarkText = Color.FromArgb(240, 240, 240);
        public static readonly Color DarkDisabledText = Color.FromArgb(130, 130, 130);

        public static Color Back { get { return IsDark ? DarkBack : SystemColors.Window; } }
        public static Color Fore { get { return IsDark ? DarkText : SystemColors.ControlText; } }

        ///
        /// Windows: dark title bars
        ///

        [DllImport("dwmapi.dll")]
        private static extern Int32 DwmSetWindowAttribute(IntPtr hwnd, Int32 attribute, ref Int32 value, Int32 size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern Int32 SetWindowTheme(IntPtr hwnd, String appName, String idList);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, Int32 msg, IntPtr wParam, IntPtr lParam);

        private const Int32 LVM_GETHEADER = 0x101F;

        /// <summary>
        /// Dark scroll bars (and list headers) where Windows has them.
        /// </summary>
        private static void ApplyNativeTheme(Control control)
        {
            Boolean dark = IsDark;
            SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);

            if (control is ListView)
            {
                IntPtr header = SendMessage(control.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                if (header != IntPtr.Zero)
                    SetWindowTheme(header, dark ? "DarkMode_ItemsView" : "ItemsView", null);
            }
        }

        /// <summary>
        /// Dark or light title bar (Windows 10 2004+ / 11, ignored elsewhere).
        /// </summary>
        public static void ApplyTitleBar(Form form)
        {
            try
            {
                Int32 dark = IsDark ? 1 : 0;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE, 19 on older Windows 10 builds:
                if (DwmSetWindowAttribute(form.Handle, 20, ref dark, 4) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref dark, 4);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// List headers keep their light look (and dark text) whatever the
        /// colors, so in dark mode they are drawn here; the rows as usual.
        /// </summary>
        private static void ApplyListView(ListView list, Boolean dark)
        {
            if (!(list.Tag is String && (String) list.Tag == "themed"))
            {
                list.Tag = "themed";
                list.DrawColumnHeader += OnDrawColumnHeader;
                list.DrawItem += (s, e) => e.DrawDefault = true;
                list.DrawSubItem += (s, e) => e.DrawDefault = true;
            }

            list.OwnerDraw = dark;
            list.Invalidate();
        }

        private static void OnDrawColumnHeader(Object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush back = new SolidBrush(DarkBack))
                e.Graphics.FillRectangle(back, e.Bounds);

            using (Pen line = new Pen(DarkBorder))
            {
                e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top + 3, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            Rectangle text = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, text, DarkText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        /// <summary>
        /// Colors for a window and everything in it.
        /// </summary>
        public static void Apply(Control control)
        {
            Boolean dark = IsDark;

            if (control is Form)
            {
                control.BackColor = dark ? DarkBack : SystemColors.Control;
                control.ForeColor = Fore;
            }
            else if (control is TextBox || control is ComboBox || control is ListView)
            {
                control.BackColor = dark ? DarkControl : SystemColors.Window;
                control.ForeColor = dark ? DarkText : SystemColors.WindowText;

                // the themed (3D) borders and combo boxes ignore BackColor:
                if (control is TextBox)
                    ((TextBox) control).BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                if (control is ComboBox)
                    ((ComboBox) control).FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                if (control is ListView)
                    ApplyListView((ListView) control, dark);

                if (control.IsHandleCreated)
                    ApplyNativeTheme(control);
                else
                    control.HandleCreated += (s, e) => ApplyNativeTheme((Control) s);
            }
            else if (control is Button)
            {
                Button button = (Button) control;
                button.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                button.BackColor = dark ? DarkControl : SystemColors.Control;
                button.ForeColor = Fore;
                button.FlatAppearance.BorderColor = DarkBorder;
                button.UseVisualStyleBackColor = !dark;
            }
            else
            {
                control.BackColor = dark ? DarkBack : SystemColors.Control;
                control.ForeColor = Fore;
            }

            foreach (Control child in control.Controls)
                Apply(child);
        }
    }
}
