using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using GameLauncher.Models;

namespace GameLauncher.Services.MainWindow
{
    public sealed class TrayController : ITrayController
    {
        private NotifyIcon? _notifyIcon;

        public void Initialize(Action onOpenRequested, Action onExitRequested)
        {
            if (onOpenRequested == null) throw new ArgumentNullException(nameof(onOpenRequested));
            if (onExitRequested == null) throw new ArgumentNullException(nameof(onExitRequested));

            try
            {
                _notifyIcon = new NotifyIcon
                {
                    Text = "Game Launcher",
                    Icon = ResolveIcon(),
                    Visible = false
                };

                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Öffnen", null, (_, _) => onOpenRequested());
                contextMenu.Items.Add("Beenden", null, (_, _) => onExitRequested());

                _notifyIcon.ContextMenuStrip = contextMenu;
                _notifyIcon.DoubleClick += (_, _) => onOpenRequested();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize tray icon", ex);
            }
        }

        public void ShowTrayIcon()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = true;
            }
        }

        public void HideTrayIcon()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
            }
        }

        public void ShowBalloon(string title, string text, int timeoutMs)
        {
            if (_notifyIcon == null)
            {
                return;
            }

            _notifyIcon.ShowBalloonTip(timeoutMs, title, text, ToolTipIcon.Info);
        }

        public void Dispose()
        {
            if (_notifyIcon == null)
            {
                return;
            }

            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            finally
            {
                _notifyIcon = null;
            }
        }

        private static Icon ResolveIcon()
        {
            try
            {
                var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrWhiteSpace(entryAssemblyPath))
                {
                    var extracted = Icon.ExtractAssociatedIcon(entryAssemblyPath);
                    if (extracted != null)
                    {
                        return extracted;
                    }
                }
            }
            catch
            {
                // Fallback below.
            }

            return SystemIcons.Application;
        }
    }
}
