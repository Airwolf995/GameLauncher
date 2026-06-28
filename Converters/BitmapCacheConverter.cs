using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using GameLauncher.Services;

namespace GameLauncher
{
    public class BitmapCacheConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrEmpty(path))
            {
                return null;
            }

            return GameImageBitmapCache.GetCachedForUi(path);
        }

        public static Task PreloadAsync(IEnumerable<string> imagePaths, CancellationToken ct = default) =>
            GameImageBitmapCache.PreloadAsync(imagePaths, ct);

        public static bool IsCachedForUi(string? path) =>
            GameImageBitmapCache.IsCached(path);

        public static void ReleaseStartupStrongCache() =>
            GameImageBitmapCache.ReleaseStartupStrongCache();

        public static void Invalidate(string path) =>
            GameImageBitmapCache.Invalidate(path);

        public static void ClearImageCaches() =>
            GameImageBitmapCache.Clear();

        public static void UpdateViewportRetention(IEnumerable<string> imagePaths) =>
            GameImageBitmapCache.UpdateViewportRetention(imagePaths);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
