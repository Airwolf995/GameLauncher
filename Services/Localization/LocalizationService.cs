using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace GameLauncher.Services.Localization
{
    public sealed class LocalizationService : INotifyPropertyChanged
    {
        private IReadOnlyDictionary<string, string> _texts = LocalizedTextCatalog.GetTexts(AppLanguage.English);
        private AppLanguage _currentLanguage = AppLanguage.English;

        public static LocalizationService Instance { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? LanguageChanged;

        public AppLanguage CurrentLanguage => _currentLanguage;

        public string this[string key] => Get(key);

        public void ApplyLanguageCode(string? languageCode)
        {
            SetLanguage(ParseLanguage(languageCode));
        }

        public void SetLanguage(AppLanguage language)
        {
            if (_currentLanguage == language)
            {
                return;
            }

            _currentLanguage = language;
            _texts = LocalizedTextCatalog.GetTexts(language);

            var culture = language == AppLanguage.German
                ? CultureInfo.GetCultureInfo("de-DE")
                : CultureInfo.GetCultureInfo("en-US");

            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string Get(string key)
        {
            if (_texts.TryGetValue(key, out var value))
            {
                return value;
            }

            var englishTexts = LocalizedTextCatalog.GetTexts(AppLanguage.English);
            return englishTexts.TryGetValue(key, out var fallback) ? fallback : key;
        }

        public string Format(string key, params object[] args) =>
            string.Format(CurrentCulture, Get(key), args);

        public CultureInfo CurrentCulture =>
            _currentLanguage == AppLanguage.German
                ? CultureInfo.GetCultureInfo("de-DE")
                : CultureInfo.GetCultureInfo("en-US");

        public static AppLanguage ParseLanguage(string? languageCode) =>
            string.Equals(languageCode, "de", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.German
                : AppLanguage.English;

        public static string ToLanguageCode(AppLanguage language) =>
            language == AppLanguage.German ? "de" : "en";
    }
}
