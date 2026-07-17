using System;
using System.Collections.Generic;
using GameLauncher.Core;
using GameLauncher.Models;
using GameLauncher.Services.Localization;

namespace GameLauncher.ViewModels.Settings
{
    public sealed class BehaviorSettingsViewModel : ObservableObject
    {
        private readonly LocalizationService _localization;
        private bool _isLoading;
        private bool _autostartEnabled;
        private bool _minimizeToTray;
        private bool _minimizeOnGameStart;
        private bool _closeOnGameStart;
        private bool _overlayHotkeyCtrl;
        private bool _overlayHotkeyAlt;
        private bool _overlayHotkeyShift;
        private bool _overlayHotkeyWin;
        private string _overlayHotkeyKey = "G";
        private bool _autoCheckUpdates;

        public BehaviorSettingsViewModel(LocalizationService localization)
        {
            _localization = localization;
        }

        public IReadOnlyList<string> OverlayHotkeyKeys { get; } = BuildOverlayHotkeyKeys();

        public bool AutostartEnabled
        {
            get => _autostartEnabled;
            set => SetProperty(ref _autostartEnabled, value);
        }

        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set => SetProperty(ref _minimizeToTray, value);
        }

        public bool MinimizeOnGameStart
        {
            get => _minimizeOnGameStart;
            set
            {
                if (SetProperty(ref _minimizeOnGameStart, value) && !_isLoading && value && CloseOnGameStart)
                {
                    CloseOnGameStart = false;
                }
            }
        }

        public bool CloseOnGameStart
        {
            get => _closeOnGameStart;
            set
            {
                if (SetProperty(ref _closeOnGameStart, value) && !_isLoading && value && MinimizeOnGameStart)
                {
                    MinimizeOnGameStart = false;
                }
            }
        }

        public bool OverlayHotkeyCtrl
        {
            get => _overlayHotkeyCtrl;
            set { if (SetProperty(ref _overlayHotkeyCtrl, value)) EnsureValidHotkey(); }
        }

        public bool OverlayHotkeyAlt
        {
            get => _overlayHotkeyAlt;
            set { if (SetProperty(ref _overlayHotkeyAlt, value)) EnsureValidHotkey(); }
        }

        public bool OverlayHotkeyShift
        {
            get => _overlayHotkeyShift;
            set { if (SetProperty(ref _overlayHotkeyShift, value)) EnsureValidHotkey(); }
        }

        public bool OverlayHotkeyWin
        {
            get => _overlayHotkeyWin;
            set { if (SetProperty(ref _overlayHotkeyWin, value)) EnsureValidHotkey(); }
        }

        public string OverlayHotkeyKey
        {
            get => _overlayHotkeyKey;
            set { if (SetProperty(ref _overlayHotkeyKey, value)) NotifyHotkeyDisplayChanged(); }
        }

        public string OverlayHotkeyDisplayText =>
            _localization.Format("Settings.CurrentHotkey", BuildOverlayHotkeyDisplay());

        public bool AutoCheckUpdates
        {
            get => _autoCheckUpdates;
            set => SetProperty(ref _autoCheckUpdates, value);
        }

        public void Load(UISettings settings, bool autostartEnabled)
        {
            _isLoading = true;
            try
            {
                AutostartEnabled = autostartEnabled;
                MinimizeToTray = settings.MinimizeToTray;
                MinimizeOnGameStart = settings.MinimizeOnGameStart;
                CloseOnGameStart = settings.CloseOnGameStart;
                OverlayHotkeyCtrl = settings.OverlayHotkeyCtrl;
                OverlayHotkeyAlt = settings.OverlayHotkeyAlt;
                OverlayHotkeyShift = settings.OverlayHotkeyShift;
                OverlayHotkeyWin = settings.OverlayHotkeyWin;
                OverlayHotkeyKey = string.IsNullOrWhiteSpace(settings.OverlayHotkeyKey) ? "G" : settings.OverlayHotkeyKey;
                AutoCheckUpdates = settings.AutoCheckUpdates;
            }
            finally
            {
                _isLoading = false;
            }
        }

        public void Reset()
        {
            _isLoading = true;
            try
            {
                MinimizeToTray = false;
                MinimizeOnGameStart = false;
                CloseOnGameStart = false;
                OverlayHotkeyCtrl = false;
                OverlayHotkeyAlt = true;
                OverlayHotkeyShift = false;
                OverlayHotkeyWin = false;
                OverlayHotkeyKey = "G";
                AutoCheckUpdates = true;
            }
            finally
            {
                _isLoading = false;
            }
        }

        public void RefreshLocalizedTexts() => NotifyHotkeyDisplayChanged();

        private void EnsureValidHotkey()
        {
            if (!_isLoading && !OverlayHotkeyCtrl && !OverlayHotkeyAlt && !OverlayHotkeyShift && !OverlayHotkeyWin)
            {
                _overlayHotkeyAlt = true;
                OnPropertyChanged(nameof(OverlayHotkeyAlt));
            }

            NotifyHotkeyDisplayChanged();
        }

        private void NotifyHotkeyDisplayChanged() => OnPropertyChanged(nameof(OverlayHotkeyDisplayText));

        private string BuildOverlayHotkeyDisplay()
        {
            var parts = new List<string>();
            if (OverlayHotkeyCtrl) parts.Add(_localization.CurrentLanguage == AppLanguage.German ? "Strg" : "Ctrl");
            if (OverlayHotkeyAlt) parts.Add("Alt");
            if (OverlayHotkeyShift) parts.Add("Shift");
            if (OverlayHotkeyWin) parts.Add("Win");
            parts.Add(string.IsNullOrWhiteSpace(OverlayHotkeyKey) ? "G" : OverlayHotkeyKey);
            return string.Join("+", parts);
        }

        private static IReadOnlyList<string> BuildOverlayHotkeyKeys()
        {
            var keys = new List<string>();
            for (char key = 'A'; key <= 'Z'; key++) keys.Add(key.ToString());
            for (int key = 0; key <= 9; key++) keys.Add(key.ToString());
            for (int key = 1; key <= 12; key++) keys.Add($"F{key}");
            return keys;
        }
    }
}
