using System;
using System.Windows;
using System.Windows.Interop;

namespace GameLauncher.Services
{
    /// <summary>
    /// Zentraler Helper für die Windows Immersive Dark Mode Titelleiste.
    /// Ersetzt die duplizierten DwmSetWindowAttribute-Aufrufe in allen Fenstern.
    /// </summary>
    public static class DarkModeHelper
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>
        /// Aktiviert die Dark Mode Titelleiste für das angegebene Fenster.
        /// Sollte nach SourceInitialized oder in Window_Loaded aufgerufen werden.
        /// </summary>
        public static void EnableDarkTitleBar(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                int darkMode = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }
            catch
            {
                // Fail silently on older Windows versions
            }
        }
    }
}
