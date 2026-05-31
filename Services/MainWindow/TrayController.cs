using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using GameLauncher.Models;
using GameLauncher.Services.Localization;

namespace GameLauncher.Services.MainWindow
{
    public sealed class TrayController : ITrayController
    {
        private readonly LocalizationService _localization = LocalizationService.Instance;
        private NotifyIcon? _notifyIcon;
        private ToolStripItem? _openItem;
        private ToolStripItem? _exitItem;

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
                _openItem = contextMenu.Items.Add(_localization.Get("Tray.Open"), null, (_, _) => onOpenRequested());
                _exitItem = contextMenu.Items.Add(_localization.Get("Tray.Exit"), null, (_, _) => onExitRequested());

                _notifyIcon.ContextMenuStrip = contextMenu;
                _notifyIcon.DoubleClick += (_, _) => onOpenRequested();
                _localization.LanguageChanged += OnLanguageChanged;
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
                _localization.LanguageChanged -= OnLanguageChanged;
            }
            finally
            {
                _notifyIcon = null;
                _openItem = null;
                _exitItem = null;
            }
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            if (_openItem != null)
            {
                _openItem.Text = _localization.Get("Tray.Open");
            }

            if (_exitItem != null)
            {
                _exitItem.Text = _localization.Get("Tray.Exit");
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
