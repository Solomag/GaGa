// GaGa.
// A minimal radio player for the Windows Tray.


using System;
using System.Windows.Forms;

using Microsoft.Win32;


namespace GaGa
{
    /// <summary>
    /// "Start with Windows": a value under the current user's Run key.
    /// Only written when the user turns the option on.
    /// </summary>
    internal static class Autostart
    {
        private const String RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const String ValueName = "GaGa";

        private static String Command
        {
            get { return "\"" + Application.ExecutablePath + "\""; }
        }

        /// <summary>
        /// Whether GaGa starts with Windows (from this exe).
        /// </summary>
        public static Boolean IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    {
                        String value = (key != null) ? key.GetValue(ValueName) as String : null;
                        return value != null && String.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Turn starting with Windows on or off. Throws on registry errors.
        /// </summary>
        public static void Set(Boolean enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled)
                    key.SetValue(ValueName, Command, RegistryValueKind.String);
                else if (key.GetValue(ValueName) != null)
                    key.DeleteValue(ValueName, false);
            }
        }
    }
}
