using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using GameLauncher.Models;

namespace GameLauncher
{
    public class BitmapCacheConverter : IValueConverter
    {
        // WeakReferences allow GC to reclaim bitmaps under memory pressure
        private static readonly ConcurrentDictionary<string, WeakReference<BitmapImage>> _cache = new();
        private static readonly ConcurrentDictionary<string, BitmapImage> _startupStrongCache = new();
        private static readonly Dictionary<string, BitmapImage> _viewportStrongCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _desiredViewportPaths = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _viewportStrongCacheLock = new();
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        private static readonly SemaphoreSlim _imageLoadLimiter = new(4);
        private static int _startupStrongCacheHits;
        private static int _weakCacheHits;
        private static int _preloadLoadCount;
        private static int _uiLoadCount;

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrEmpty(path))
                return null;

            return LoadAndCacheBitmap(path, loadOrigin: "ui");
        }

        public static async Task PreloadAsync(IEnumerable<string> imagePaths, CancellationToken ct = default)
        {
            var paths = imagePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !IsCached(path))
                .ToList();

            if (paths.Count == 0)
            {
                Logger.Log("Image preload skipped: all cover images are already cached.");
                return;
            }

            var watch = Stopwatch.StartNew();
            ResetCacheStats();
            Logger.Log($"Image preload started: {paths.Count} cover image(s).");

            await Parallel.ForEachAsync(
                paths,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 4,
                    CancellationToken = ct
                },
                (path, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    LoadAndCacheBitmap(path, keepStrongReference: true, loadOrigin: "preload");
                    return ValueTask.CompletedTask;
                });

            watch.Stop();
            Logger.Log($"Image preload completed: {paths.Count} cover image(s) in {watch.ElapsedMilliseconds} ms.");
        }

        private static bool IsCached(string path) =>
            _startupStrongCache.ContainsKey(path) ||
            (_cache.TryGetValue(path, out var weakRef) && weakRef.TryGetTarget(out _));

        private static BitmapImage? LoadAndCacheBitmap(string path, bool keepStrongReference = false, string loadOrigin = "ui")
        {
            if (_startupStrongCache.TryGetValue(path, out var strongCached))
            {
                Interlocked.Increment(ref _startupStrongCacheHits);
                return strongCached;
            }

            if (TryGetViewportStrongCache(path, out var viewportCached))
            {
                var cachedBitmap = viewportCached!;
                Interlocked.Increment(ref _weakCacheHits);
                if (keepStrongReference)
                {
                    _startupStrongCache[path] = cachedBitmap;
                }
                return cachedBitmap;
            }

            // Return cached bitmap if still alive
            if (_cache.TryGetValue(path, out var weakRef) && weakRef.TryGetTarget(out var cached))
            {
                Interlocked.Increment(ref _weakCacheHits);
                if (keepStrongReference)
                {
                    _startupStrongCache[path] = cached;
                }
                return cached;
            }
            
            if (string.Equals(loadOrigin, "preload", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _preloadLoadCount);
            }
            else
            {
                Interlocked.Increment(ref _uiLoadCount);
            }

            try
            {
                Uri uri = new Uri(path);
                BitmapImage bitmap;

                _imageLoadLimiter.Wait();
                try
                {
                if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                {
                    bitmap = LoadRemoteBitmap(uri);
                    Logger.Log($"[BitmapCache] Remote Image Loaded: {path} ({bitmap.PixelWidth}x{bitmap.PixelHeight}) - Decoded & Frozen");
                }
                else
                {
                    // Local file: load fully into memory to unlock the file
                    bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = uri;
                    bitmap.DecodePixelWidth = 400; // Optimize RAM for local manual covers
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    Logger.Log($"[BitmapCache] Local Image Loaded: {path} ({bitmap.PixelWidth}x{bitmap.PixelHeight}) - Decoded to 400px width");
                }
                }
                finally
                {
                    _imageLoadLimiter.Release();
                }

                if (keepStrongReference)
                {
                    _startupStrongCache[path] = bitmap;
                }
                TryPinViewportPath(path, bitmap);
                _cache[path] = new WeakReference<BitmapImage>(bitmap);
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static BitmapImage LoadRemoteBitmap(Uri uri)
        {
            byte[] bytes = _httpClient.GetByteArrayAsync(uri).GetAwaiter().GetResult();
            using var stream = new MemoryStream(bytes);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.DecodePixelWidth = 400;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>
        /// Releases the temporary startup cache after the first card animation completed.
        /// </summary>
        public static void ReleaseStartupStrongCache()
        {
            if (_startupStrongCache.IsEmpty)
            {
                return;
            }

            int releasedCount = _startupStrongCache.Count;
            _startupStrongCache.Clear();
            Logger.Log($"Image preload strong cache released: {releasedCount} cover image(s).");
            Logger.Log(
                $"Image cache stats: startup strong hits={_startupStrongCacheHits}, weak hits={_weakCacheHits}, preload loads={_preloadLoadCount}, ui reloads={_uiLoadCount}.");
        }

        /// <summary>
        /// Removes a specific path from the cache (e.g. after image update).
        /// </summary>
        public static void Invalidate(string path)
        {
            _startupStrongCache.TryRemove(path, out _);
            _cache.TryRemove(path, out _);
            RemoveFromViewportStrongCache(path);
        }

        public static void ClearImageCaches()
        {
            _startupStrongCache.Clear();
            _cache.Clear();

            lock (_viewportStrongCacheLock)
            {
                _viewportStrongCache.Clear();
                _desiredViewportPaths.Clear();
            }

            Logger.Log("Image caches cleared for manual library refresh.");
        }

        public static void UpdateViewportRetention(IEnumerable<string> imagePaths)
        {
            var desiredPaths = imagePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (_viewportStrongCacheLock)
            {
                _desiredViewportPaths.Clear();
                foreach (var path in desiredPaths)
                {
                    _desiredViewportPaths.Add(path);
                }

                var toRemove = _viewportStrongCache.Keys
                    .Where(path => !_desiredViewportPaths.Contains(path))
                    .ToList();

                foreach (var path in toRemove)
                {
                    _viewportStrongCache.Remove(path);
                }

                foreach (var path in desiredPaths)
                {
                    if (_viewportStrongCache.ContainsKey(path))
                    {
                        continue;
                    }

                    if (_startupStrongCache.TryGetValue(path, out var startupBitmap))
                    {
                        _viewportStrongCache[path] = startupBitmap;
                        continue;
                    }

                    if (_cache.TryGetValue(path, out var weakRef) && weakRef.TryGetTarget(out var cached))
                    {
                        _viewportStrongCache[path] = cached;
                    }
                }
            }
        }

        private static void ResetCacheStats()
        {
            Interlocked.Exchange(ref _startupStrongCacheHits, 0);
            Interlocked.Exchange(ref _weakCacheHits, 0);
            Interlocked.Exchange(ref _preloadLoadCount, 0);
            Interlocked.Exchange(ref _uiLoadCount, 0);
        }

        private static bool TryGetViewportStrongCache(string path, out BitmapImage? bitmap)
        {
            lock (_viewportStrongCacheLock)
            {
                if (_viewportStrongCache.TryGetValue(path, out var cached))
                {
                    bitmap = cached;
                    return true;
                }
            }

            bitmap = null;
            return false;
        }

        private static void TryPinViewportPath(string path, BitmapImage bitmap)
        {
            lock (_viewportStrongCacheLock)
            {
                if (_desiredViewportPaths.Contains(path))
                {
                    _viewportStrongCache[path] = bitmap;
                }
            }
        }

        private static void RemoveFromViewportStrongCache(string path)
        {
            lock (_viewportStrongCacheLock)
            {
                _viewportStrongCache.Remove(path);
                _desiredViewportPaths.Remove(path);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
