using System;
using System.Runtime.InteropServices;
using System.Text;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Liest Ziel, Argumente und Arbeitsverzeichnis einer Windows-Verknüpfung.
    /// Verwendet die Shell-Schnittstelle IShellLink statt des Windows Script Host,
    /// da dieser in verwalteten Umgebungen regelmäßig deaktiviert ist.
    /// </summary>
    internal static class ShortcutResolver
    {
        private const int MaxPathLength = 260;
        private const int MaxArgumentsLength = 1024;

        /// <summary>
        /// Löst eine .lnk-Datei auf. Liefert null, wenn die Verknüpfung nicht
        /// gelesen werden kann oder kein Ziel besitzt.
        /// </summary>
        public static ShortcutTarget? TryResolve(string shortcutPath)
        {
            object? shellLinkInstance = null;
            try
            {
                shellLinkInstance = new ShellLink();
                var persistFile = (IPersistFile)shellLinkInstance;
                persistFile.Load(shortcutPath, 0);

                var shellLink = (IShellLinkW)shellLinkInstance;

                var targetBuilder = new StringBuilder(MaxPathLength);
                shellLink.GetPath(targetBuilder, targetBuilder.Capacity, IntPtr.Zero, 0);
                string targetPath = targetBuilder.ToString();

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    return null;
                }

                var argumentsBuilder = new StringBuilder(MaxArgumentsLength);
                shellLink.GetArguments(argumentsBuilder, argumentsBuilder.Capacity);

                var workingDirectoryBuilder = new StringBuilder(MaxPathLength);
                shellLink.GetWorkingDirectory(workingDirectoryBuilder, workingDirectoryBuilder.Capacity);

                return new ShortcutTarget(
                    targetPath,
                    argumentsBuilder.ToString(),
                    workingDirectoryBuilder.ToString());
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
            {
                Logger.Log($"Verknüpfung konnte nicht gelesen werden: {shortcutPath} ({ex.GetType().Name})");
                return null;
            }
            finally
            {
                if (shellLinkInstance != null && Marshal.IsComObject(shellLinkInstance))
                {
                    Marshal.FinalReleaseComObject(shellLinkInstance);
                }
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath(
                [MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
                int maxPath,
                IntPtr findData,
                uint flags);

            void GetIDList(out IntPtr idList);
            void SetIDList(IntPtr idList);

            void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

            void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

            void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxArguments);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);

            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);

            void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int iconIndex);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
            void Resolve(IntPtr window, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }

        [ComImport]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid classId);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string? fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
        }
    }

    /// <summary>
    /// Aufgelöstes Ziel einer Windows-Verknüpfung.
    /// </summary>
    internal sealed record ShortcutTarget(string TargetPath, string Arguments, string WorkingDirectory);
}
