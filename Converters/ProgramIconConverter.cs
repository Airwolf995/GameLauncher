using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameLauncher.Models;

namespace GameLauncher.Converters
{
    /// <summary>
    /// Liefert das Symbol einer Programmdatei für die Anzeige in Listen.
    /// Anders als der IconExtractor der Scanner wird dabei nichts auf die
    /// Festplatte geschrieben: Die Auswahllisten zeigen Programme, die der
    /// Benutzer gar nicht übernimmt, und sollen dafür keine Dateien anlegen.
    /// </summary>
    public sealed class ProgramIconConverter : IValueConverter
    {
        private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            lock (Cache)
            {
                if (Cache.TryGetValue(path, out ImageSource? cached))
                {
                    return cached;
                }
            }

            ImageSource? icon = TryLoadIcon(path);

            lock (Cache)
            {
                Cache[path] = icon;
            }

            return icon;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        private static ImageSource? TryLoadIcon(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null)
                {
                    return null;
                }

                var source = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch (Exception ex)
            {
                Logger.Log($"Symbol konnte nicht gelesen werden: {path} ({ex.GetType().Name})");
                return null;
            }
        }
    }
}
