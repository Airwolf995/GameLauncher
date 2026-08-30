using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GameLauncher.Services
{
    /// <summary>
    /// Ermittelt den Programmpfad eines laufenden Prozesses.
    /// Verwendet QueryFullProcessImageName statt Process.MainModule: Letzteres
    /// verlangt Leserechte auf den Adressraum des Prozesses, scheitert deshalb an
    /// geschützten Prozessen und meldet das über eine Ausnahme. Bei mehreren
    /// hundert Prozessen je Durchlauf ist das sowohl langsamer als auch weniger
    /// ergiebig als die hier genutzte Abfrage.
    /// </summary>
    internal static class ProcessPathReader
    {
        private const int ProcessQueryLimitedInformation = 0x1000;
        private const int InitialBufferLength = 1024;

        /// <summary>
        /// Liefert den vollständigen Programmpfad oder null, wenn der Prozess
        /// beendet ist oder der Zugriff verweigert wird.
        /// </summary>
        public static string? TryGetExecutablePath(int processId)
        {
            if (processId <= 0)
            {
                return null;
            }

            IntPtr processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (processHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                int bufferLength = InitialBufferLength;
                var buffer = new StringBuilder(bufferLength);

                return QueryFullProcessImageName(processHandle, 0, buffer, ref bufferLength)
                    ? buffer.ToString(0, bufferLength)
                    : null;
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr processHandle, int flags, StringBuilder buffer, ref int bufferLength);
    }
}
