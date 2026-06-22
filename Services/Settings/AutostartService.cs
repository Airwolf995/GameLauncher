using System;
using System.Diagnostics;
using GameLauncher.Models;
using Microsoft.Win32;

namespace GameLauncher.Services.Settings
{
    internal sealed class AutostartService : IAutostartService
    {
        private const string ApplicationName = "GameLauncher";
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        public bool IsEnabled(bool fallbackValue)
        {
            try
            {
                using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                if (runKey?.GetValue(ApplicationName) == null)
                {
                    return false;
                }

                using RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, false);
                byte[]? approvedValue = approvedKey?.GetValue(ApplicationName) as byte[];
                return approvedValue == null || approvedValue.Length == 0 || approvedValue[0] == 0x02;
            }
            catch (Exception ex)
            {
                Logger.Error("Autostart registry check failed", ex);
                return fallbackValue;
            }
        }

        public void SetEnabled(bool enabled)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                string? applicationPath = Process.GetCurrentProcess().MainModule?.FileName;
                if (key == null || string.IsNullOrWhiteSpace(applicationPath))
                {
                    return;
                }

                if (enabled)
                {
                    key.SetValue(ApplicationName, $"\"{applicationPath}\"");
                }
                else
                {
                    key.DeleteValue(ApplicationName, false);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Autostart registry update failed", ex);
            }
        }
    }
}
