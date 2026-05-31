using System;
using System.Globalization;
using System.Windows.Data;
using GameLauncher.Models;
using GameLauncher.Services.Localization;

namespace GameLauncher.Converters
{
    public sealed class EnumLocalizationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                CardSize.Small => LocalizationService.Instance.Get("CardSize.Small"),
                CardSize.Medium => LocalizationService.Instance.Get("CardSize.Medium"),
                CardSize.Large => LocalizationService.Instance.Get("CardSize.Large"),
                ViewMode.Cards => LocalizationService.Instance.Get("ViewMode.Cards"),
                ViewMode.List => LocalizationService.Instance.Get("ViewMode.List"),
                _ => value?.ToString() ?? string.Empty
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
