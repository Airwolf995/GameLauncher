using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GameLauncher.Converters
{
    /// <summary>
    /// Zeigt genau die Seite, deren Schluessel in der Kategorienleiste ausgewaehlt
    /// ist. Die Seiten liegen alle im selben Grid uebereinander; ohne diesen
    /// Vergleich braeuchte jede von ihnen eine eigene Eigenschaft im ViewModel.
    /// </summary>
    public class TagMatchToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var selected = value as string;
            var page = parameter as string;
            return string.Equals(selected, page, StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
