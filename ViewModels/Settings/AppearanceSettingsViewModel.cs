using System;
using System.Collections.Generic;
using System.Linq;
using GameLauncher.Core;
using GameLauncher.Models;
using GameLauncher.Services.Localization;

namespace GameLauncher.ViewModels.Settings
{
    public sealed class AppearanceSettingsViewModel : ObservableObject
    {
        private readonly Action _previewChanged;
        private readonly Action<string> _themeChanged;
        private bool _suppressCallbacks;
        private string _selectedTheme = "";
        private string _selectedLanguageCode = "en";
        private CardSize _cardSize;
        private ViewMode _viewMode;
        private bool _animationsEnabled;
        private double _fontScale;

        public AppearanceSettingsViewModel(Action previewChanged, Action<string> themeChanged)
        {
            _previewChanged = previewChanged;
            _themeChanged = themeChanged;
        }

        public IEnumerable<CardSize> CardSizeOptions => Enum.GetValues(typeof(CardSize)).Cast<CardSize>();
        public IEnumerable<ViewMode> ViewModeOptions => Enum.GetValues(typeof(ViewMode)).Cast<ViewMode>();

        public string SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (SetProperty(ref _selectedTheme, value) && !_suppressCallbacks)
                {
                    string colorCode = Constants.UI.GetColorCodeForTheme(value);
                    if (!string.IsNullOrEmpty(colorCode))
                    {
                        _themeChanged(colorCode);
                    }
                }
            }
        }

        public string SelectedLanguageCode
        {
            get => _selectedLanguageCode;
            set => SetProperty(
                ref _selectedLanguageCode,
                string.Equals(value, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en");
        }

        public CardSize CardSize
        {
            get => _cardSize;
            set
            {
                if (SetProperty(ref _cardSize, value) && !_suppressCallbacks)
                {
                    _previewChanged();
                }
            }
        }

        public ViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                if (SetProperty(ref _viewMode, value) && !_suppressCallbacks)
                {
                    _previewChanged();
                }
            }
        }

        public bool AnimationsEnabled
        {
            get => _animationsEnabled;
            set
            {
                if (SetProperty(ref _animationsEnabled, value) && !_suppressCallbacks)
                {
                    _previewChanged();
                }
            }
        }

        public double FontScale
        {
            get => _fontScale;
            set
            {
                if (SetProperty(ref _fontScale, value))
                {
                    OnPropertyChanged(nameof(FontScalePercentage));
                    if (!_suppressCallbacks)
                    {
                        _previewChanged();
                    }
                }
            }
        }

        public string FontScalePercentage => $"{(int)(FontScale * 100)}%";
        public string BackgroundImage { get; set; } = "";

        public void Load(GameConfig config)
        {
            _suppressCallbacks = true;
            try
            {
                SelectedTheme = Constants.UI.NormalizeThemeKey(config.Theme);
                SelectedLanguageCode = config.UISettings.LanguageCode;
                CardSize = config.UISettings.CardSize;
                ViewMode = config.UISettings.ViewMode;
                AnimationsEnabled = config.UISettings.AnimationsEnabled;
                FontScale = config.UISettings.FontScale > 0 ? config.UISettings.FontScale : 1.0;
                BackgroundImage = config.UISettings.BackgroundImage ?? "";
            }
            finally
            {
                _suppressCallbacks = false;
            }
        }

        public void Reset()
        {
            _suppressCallbacks = true;
            try
            {
                SelectedTheme = "Blue";
                SelectedLanguageCode = "en";
                CardSize = CardSize.Medium;
                ViewMode = ViewMode.Cards;
                AnimationsEnabled = true;
                FontScale = 1.0;
                BackgroundImage = "";
            }
            finally
            {
                _suppressCallbacks = false;
            }
        }
    }
}
