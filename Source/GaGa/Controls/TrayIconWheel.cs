
// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;


namespace GaGa.Controls
{
    /// <summary>
    /// Reports the mouse wheel over a tray icon. Windows doesn't send wheel
    /// messages to tray icons, so a low-level mouse hook watches the wheel
    /// and checks whether the cursor is over the icon. The wheel event is
    /// passed on untouched.
    /// </summary>
    internal class TrayIconWheel : IDisposable
    {
        /// <summary>Raised on the UI thread with +1 per notch up, -1 per notch down.</summary>
        public event Action<Int32> Wheel;

        private readonly NotifyIcon notifyIcon;
        private readonly HookProc hookProc;  // kept alive, the hook calls it
        private IntPtr hook;
        private Int32 remainder;

        public TrayIconWheel(NotifyIcon notifyIcon)
        {
            this.notifyIcon = notifyIcon;
            hookProc = HookCallback;

            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
            {
                hook = SetWindowsHookEx(WH_MOUSE_LL, hookProc, GetModuleHandle(module.ModuleName), 0);
            }
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(Int32 code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0 && (Int32) wParam == WM_MOUSEWHEEL)
                {
                    MSLLHOOKSTRUCT info = (MSLLHOOKSTRUCT) Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    Rectangle rect;

                    if (TryGetIconRect(out rect) && rect.Contains(info.pt.x, info.pt.y))
                    {
                        Int32 delta = (Int16) (info.mouseData >> 16);
                        remainder += delta;

                        Int32 notches = remainder / 120;
                        remainder -= notches * 120;

                        Action<Int32> handler = Wheel;
                        if (notches != 0 && handler != null)
                            handler(notches);
                    }
                }
            }
            catch
            {
                // never break the mouse for everyone because of us
            }

            return CallNextHookEx(hook, code, wParam, lParam);
        }

        /// <summary>
        /// Screen rectangle of the icon (fails when it's hidden in the overflow area).
        /// NotifyIcon keeps its window and id private, so they are read by reflection.
        /// </summary>
        private Boolean TryGetIconRect(out Rectangle rect)
        {
            rect = Rectangle.Empty;

            FieldInfo windowField = typeof(NotifyIcon).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo idField = typeof(NotifyIcon).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
            if (windowField == null || idField == null)
                return false;

            NativeWindow window = windowField.GetValue(notifyIcon) as NativeWindow;
            if (window == null)
                return false;

            NOTIFYICONIDENTIFIER identifier = new NOTIFYICONIDENTIFIER();
            identifier.cbSize = (UInt32) Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));
            identifier.hWnd = window.Handle;
            identifier.uID = (UInt32) (Int32) idField.GetValue(notifyIcon);

            RECT r;
            if (Shell_NotifyIconGetRect(ref identifier, out r) != 0)
                return false;

            rect = Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom);
            return true;
        }

        private const Int32 WH_MOUSE_LL = 14;
        private const Int32 WM_MOUSEWHEEL = 0x020A;

        private delegate IntPtr HookProc(Int32 code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public Int32 x; public Int32 y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public Int32 left; public Int32 top; public Int32 right; public Int32 bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public UInt32 mouseData;
            public UInt32 flags;
            public UInt32 time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public UInt32 cbSize;
            public IntPtr hWnd;
            public UInt32 uID;
            public Guid guidItem;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(Int32 idHook, HookProc lpfn, IntPtr hMod, UInt32 dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern Boolean UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, Int32 nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(String lpModuleName);

        [DllImport("shell32.dll")]
        private static extern Int32 Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);
    }
}
