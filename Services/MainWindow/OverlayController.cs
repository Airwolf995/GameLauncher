using System;
using System.Windows;
using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.Services.MainWindow
{
    public sealed class OverlayController : IOverlayController
    {
        private HardwareMonitorService? _hardwareMonitorService;
        private HotkeyService? _hotkeyService;
        private OverlayWindow? _overlayWindow;
        private Window? _owner;
        private PlayTimeService? _playTimeService;

        public void Initialize(Window owner, PlayTimeService playTimeService)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _playTimeService = playTimeService ?? throw new ArgumentNullException(nameof(playTimeService));

            try
            {
                _hotkeyService = new HotkeyService();
                _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize overlay system", ex);
            }
        }

        public void RegisterHotkey(Window owner, UISettings settings)
        {
            try
            {
                _hotkeyService?.Register(owner, settings);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to register overlay hotkey", ex);
            }
        }

        public void Stop()
        {
            if (_hotkeyService != null)
            {
                _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
            }

            _hotkeyService?.Dispose();
            _hotkeyService = null;

            if (_overlayWindow != null)
            {
                _overlayWindow.Close();
            }
            _overlayWindow = null;

            _hardwareMonitorService?.Dispose();
            _hardwareMonitorService = null;
            _playTimeService = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private void OnHotkeyPressed()
        {
            if (_owner == null || _playTimeService == null)
            {
                return;
            }

            _owner.Dispatcher.Invoke(() =>
            {
                EnsureOverlayCreated();

                if (_overlayWindow == null)
                {
                    return;
                }

                if (_overlayWindow.Visibility == Visibility.Visible)
                {
                    _overlayWindow.Hide();
                    Logger.Log("Overlay hidden via hotkey.");
                }
                else
                {
                    _overlayWindow.Show();
                    Logger.Log("Overlay shown via hotkey.");
                }
            });
        }

        private void EnsureOverlayCreated()
        {
            if (_overlayWindow != null || _playTimeService == null)
            {
                return;
            }

            try
            {
                _hardwareMonitorService = new HardwareMonitorService();
                _overlayWindow = new OverlayWindow(_hardwareMonitorService, _playTimeService);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize overlay window", ex);
                _hardwareMonitorService?.Dispose();
                _hardwareMonitorService = null;
                _overlayWindow = null;
            }
        }
    }
}
