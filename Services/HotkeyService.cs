using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GameLauncher.Models;
using System.Windows.Input;

namespace GameLauncher.Services
{
    public class HotkeyService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint VK_G = 0x47;
        private const int HOTKEY_ID = 9000;

        private IntPtr _windowHandle;
        private HwndSource _source = null!;
        private bool _isRegistered;
        private uint _registeredModifiers;
        private uint _registeredVirtualKey;

        public event Action? HotkeyPressed;

        public void Register(Window window, UISettings settings)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var helper = new WindowInteropHelper(window);
            var windowHandle = helper.Handle;

            if (!TryBuildHotkey(settings, out var modifiers, out var virtualKey, out var displayText))
            {
                Logger.Error("Overlay hotkey configuration is invalid. Falling back to Alt+G.");
                modifiers = MOD_ALT;
                virtualKey = VK_G;
                displayText = "Alt+G";
            }

            if (_isRegistered &&
                _windowHandle == windowHandle &&
                _registeredModifiers == modifiers &&
                _registeredVirtualKey == virtualKey)
            {
                return;
            }

            UnregisterCurrentHotkey();

            _windowHandle = windowHandle;
            _source = HwndSource.FromHwnd(_windowHandle);
            _source.AddHook(HwndHook);

            if (!RegisterHotKey(_windowHandle, HOTKEY_ID, modifiers, virtualKey))
            {
                Logger.Error($"Failed to register global hotkey {displayText}. Error code: {Marshal.GetLastWin32Error()}");
            }
            else
            {
                _isRegistered = true;
                _registeredModifiers = modifiers;
                _registeredVirtualKey = virtualKey;
                Logger.Log($"Global hotkey {displayText} registered successfully.");
            }
        }

        internal static bool TryBuildHotkey(UISettings settings, out uint modifiers, out uint virtualKey, out string displayText)
        {
            modifiers = 0;
            virtualKey = 0;
            displayText = string.Empty;

            if (settings.OverlayHotkeyCtrl)
            {
                modifiers |= MOD_CONTROL;
            }

            if (settings.OverlayHotkeyAlt)
            {
                modifiers |= MOD_ALT;
            }

            if (settings.OverlayHotkeyShift)
            {
                modifiers |= MOD_SHIFT;
            }

            if (settings.OverlayHotkeyWin)
            {
                modifiers |= MOD_WIN;
            }

            if (modifiers == 0)
            {
                return false;
            }

            if (!TryGetVirtualKey(settings.OverlayHotkeyKey, out virtualKey, out var normalizedKey))
            {
                return false;
            }

            displayText = BuildDisplayText(settings, normalizedKey);
            return true;
        }

        internal static bool TryGetVirtualKey(string? keyText, out uint virtualKey, out string normalizedKey)
        {
            virtualKey = 0;
            normalizedKey = string.Empty;

            if (string.IsNullOrWhiteSpace(keyText))
            {
                return false;
            }

            normalizedKey = keyText.Trim().ToUpperInvariant();
            var keyName = normalizedKey.Length == 1 && char.IsDigit(normalizedKey[0])
                ? $"D{normalizedKey}"
                : normalizedKey;

            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key))
            {
                return false;
            }

            var keyCode = KeyInterop.VirtualKeyFromKey(key);
            if (keyCode <= 0)
            {
                return false;
            }

            virtualKey = (uint)keyCode;
            return true;
        }

        internal static string BuildDisplayText(UISettings settings, string normalizedKey)
        {
            var parts = new System.Collections.Generic.List<string>();

            if (settings.OverlayHotkeyCtrl) parts.Add("Strg");
            if (settings.OverlayHotkeyAlt) parts.Add("Alt");
            if (settings.OverlayHotkeyShift) parts.Add("Shift");
            if (settings.OverlayHotkeyWin) parts.Add("Win");

            parts.Add(normalizedKey);
            return string.Join("+", parts);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                HotkeyPressed?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterCurrentHotkey();
        }

        private void UnregisterCurrentHotkey()
        {
            _source?.RemoveHook(HwndHook);
            if (_isRegistered && _windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, HOTKEY_ID);
            }

            _isRegistered = false;
            _registeredModifiers = 0;
            _registeredVirtualKey = 0;
            _source = null!;
            _windowHandle = IntPtr.Zero;
        }
    }
}
