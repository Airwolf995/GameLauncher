using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using GameLauncher.Models;

namespace GameLauncher.Services
{
    internal static class GameImageBitmapCache
    {
        private const int DecodePixelWidth = 400;
        private const int MaxParallelImageLoads = 4;
        private const int MaxStrongCacheEntries = 384;
        private const string UiLoadOrigin = "ui";
        private const string PreloadLoadOrigin = "preload";

        private static readonly ConcurrentDictionary<string, WeakReference<BitmapImage>> WeakCache = new();
        private static readonly ConcurrentDictionary<string, BitmapImage> StartupStrongCache = new();
        private static readonly Dictionary<string, BitmapImage> MemoryStrongCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> MemoryStrongCacheLru = [];
        private static readonly Dictionary<string, LinkedListNode<string>> MemoryStrongCacheNodes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object MemoryStrongCacheLock = new();
        private static readonly Dictionary<string, BitmapImage> ViewportStrongCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> DesiredViewportPaths = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object ViewportStrongCacheLock = new();
        private static readonly ConcurrentDictionary<string, object> PathLoadLocks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        private static readonly SemaphoreSlim ImageLoadLimiter = new(MaxParallelImageLoads);

        private static int _startupStrongCacheHits;
        private static int _weakCacheHits;
        private static int _preloadLoadCount;
        private static int _uiLoadCount;

        public static BitmapImage? LoadForUi(string path) =>
            LoadAndCacheBitmap(path, keepStrongReference: false, loadOrigin: UiLoadOrigin);

        public static async Task PreloadAsync(IEnumerable<string> imagePaths, CancellationToken ct = default)
        {
            var pathsToLoad = imagePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !IsCached(path))
                .ToList();

            if (pathsToLoad.Count == 0)
            {
                Logger.Log("Image preload skipped: all cover images are already cached.");
                return;
            }

            ResetCacheStats();
            var watch = Stopwatch.StartNew();
            Logger.Log($"Image preload started: {pathsToLoad.Count} cover image(s).");

            await Parallel.ForEachAsync(
                pathsToLoad,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxParallelImageLoads,
                    CancellationToken = ct
                },
                (path, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    LoadAndCacheBitmap(path, keepStrongReference: true, loadOrigin: PreloadLoadOrigin);
                    return ValueTask.CompletedTask;
                });

            watch.Stop();
            Logger.Log($"Image preload completed: {pathsToLoad.Count} cover image(s) in {watch.ElapsedMilliseconds} ms.");
        }

        public static bool IsCached(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return StartupStrongCache.ContainsKey(path) ||
                   TryGetMemoryStrongCache(path, out _) ||
                   TryGetViewportStrongCache(path, out _) ||
                   (WeakCache.TryGetValue(path, out var weakRef) && weakRef.TryGetTarget(out _));
        }

        public static void ReleaseStartupStrongCache()
        {
            if (StartupStrongCache.IsEmpty)
            {
                return;
            }

            int releasedCount = StartupStrongCache.Count;
            StartupStrongCache.Clear();
            Logger.Log($"Image preload strong cache released: {releasedCount} cover image(s).");
            Logger.Log(
                $"Image cache stats: startup strong hits={_startupStrongCacheHits}, weak hits={_weakCacheHits}, preload loads={_preloadLoadCount}, ui reloads={_uiLoadCount}.");
        }

        public static void Invalidate(string path)
        {
            StartupStrongCache.TryRemove(path, out _);
            WeakCache.TryRemove(path, out _);
            RemoveFromMemoryStrongCache(path);
            RemoveFromViewportStrongCache(path);
        }

        public static void Clear()
        {
            StartupStrongCache.Clear();
            WeakCache.Clear();
            ClearMemoryStrongCache();

            lock (ViewportStrongCacheLock)
            {
                ViewportStrongCache.Clear();
                DesiredViewportPaths.Clear();
            }

            Logger.Log("Image caches cleared for manual library refresh.");
        }

        public static void UpdateViewportRetention(IEnumerable<string> imagePaths)
        {
            var desiredPaths = imagePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (ViewportStrongCacheLock)
            {
                DesiredViewportPaths.Clear();
                foreach (var path in desiredPaths)
                {
                    DesiredViewportPaths.Add(path);
                }

                var pathsToRemove = ViewportStrongCache.Keys
                    .Where(path => !DesiredViewportPaths.Contains(path))
                    .ToList();

                foreach (var path in pathsToRemove)
                {
                    ViewportStrongCache.Remove(path);
                }

                foreach (var path in desiredPaths)
                {
                    if (ViewportStrongCache.ContainsKey(path))
                    {
                        continue;
                    }

                    if (StartupStrongCache.TryGetValue(path, out var startupBitmap))
                    {
                        ViewportStrongCache[path] = startupBitmap;
                        continue;
                    }

                    if (TryGetMemoryStrongCache(path, out var strongBitmap))
                    {
                        ViewportStrongCache[path] = strongBitmap!;
                        continue;
                    }

                    if (WeakCache.TryGetValue(path, out var weakRef) && weakRef.TryGetTarget(out var cachedBitmap))
                    {
                        ViewportStrongCache[path] = cachedBitmap;
                    }
                }
            }
        }

        private static BitmapImage? LoadAndCacheBitmap(string path, bool keepStrongReference, string loadOrigin)
        {
            if (TryGetCachedBitmap(path, keepStrongReference, out var cachedBitmap))
            {
                return cachedBitmap;
            }

            object pathLock = PathLoadLocks.GetOrAdd(path, static _ => new object());
            lock (pathLock)
            {
                if (TryGetCachedBitmap(path, keepStrongReference, out cachedBitmap))
                {
                    return cachedBitmap;
                }

                TrackLoadOrigin(loadOrigin);

                try
                {
                    using var loadScope = new ImageLoadScope(ImageLoadLimiter);
                    BitmapImage bitmap = LoadBitmap(path);

                    if (keepStrongReference)
                    {
                        StartupStrongCache[path] = bitmap;
                    }

                    AddToMemoryStrongCache(path, bitmap);
                    TryPinViewportPath(path, bitmap);
                    WeakCache[path] = new WeakReference<BitmapImage>(bitmap);
                    return bitmap;
                }
                catch
                {
                    return null;
                }
            }
        }

        private static bool TryGetCachedBitmap(string path, bool keepStrongReference, out BitmapImage? bitmap)
        {
            if (StartupStrongCache.TryGetValue(path, out var startupBitmap))
            {
                Interlocked.Increment(ref _startupStrongCacheHits);
                bitmap = startupBitmap;
                AddToMemoryStrongCache(path, bitmap);
                return true;
            }

            if (TryGetMemoryStrongCache(path, out var strongBitmap))
            {
                Interlocked.Increment(ref _weakCacheHits);
                bitmap = strongBitmap;
                PromoteToStartupCache(path, bitmap, keepStrongReference);
                return true;
            }

            if (TryGetViewportStrongCache(path, out var viewportBitmap))
            {
                Interlocked.Increment(ref _weakCacheHits);
                bitmap = viewportBitmap!;
                AddToMemoryStrongCache(path, bitmap);
                PromoteToStartupCache(path, bitmap, keepStrongReference);
                return true;
            }

            if (WeakCache.TryGetValue(path, out var weakRef) && weakRef.TryGetTarget(out var weakBitmap))
            {
                Interlocked.Increment(ref _weakCacheHits);
                bitmap = weakBitmap;
                AddToMemoryStrongCache(path, bitmap);
                PromoteToStartupCache(path, bitmap, keepStrongReference);
                return true;
            }

            bitmap = null;
            return false;
        }

        private static void PromoteToStartupCache(string path, BitmapImage? bitmap, bool keepStrongReference)
        {
            if (!keepStrongReference || bitmap == null)
            {
                return;
            }

            StartupStrongCache[path] = bitmap;
        }

        private static void TrackLoadOrigin(string loadOrigin)
        {
            if (string.Equals(loadOrigin, PreloadLoadOrigin, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _preloadLoadCount);
                return;
            }

            Interlocked.Increment(ref _uiLoadCount);
        }

        private static BitmapImage LoadBitmap(string path)
        {
            Uri uri = new(path);
            bool isRemote = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
            BitmapImage bitmap = isRemote ? LoadRemoteBitmap(uri) : LoadLocalBitmap(uri);

            Logger.Log(
                isRemote
                    ? $"[BitmapCache] Remote Image Loaded: {path} ({bitmap.PixelWidth}x{bitmap.PixelHeight}) - Decoded & Frozen"
                    : $"[BitmapCache] Local Image Loaded: {path} ({bitmap.PixelWidth}x{bitmap.PixelHeight}) - Decoded to {DecodePixelWidth}px width");

            return bitmap;
        }

        private static BitmapImage LoadRemoteBitmap(Uri uri)
        {
            byte[] bytes = HttpClient.GetByteArrayAsync(uri).GetAwaiter().GetResult();
            using var stream = new MemoryStream(bytes);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.DecodePixelWidth = DecodePixelWidth;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static BitmapImage LoadLocalBitmap(Uri uri)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = uri;
            bitmap.DecodePixelWidth = DecodePixelWidth;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
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
            lock (ViewportStrongCacheLock)
            {
                if (ViewportStrongCache.TryGetValue(path, out var cached))
                {
                    bitmap = cached;
                    return true;
                }
            }

            bitmap = null;
            return false;
        }

        private static bool TryGetMemoryStrongCache(string path, out BitmapImage? bitmap)
        {
            lock (MemoryStrongCacheLock)
            {
                if (MemoryStrongCache.TryGetValue(path, out var cached))
                {
                    TouchMemoryStrongCacheEntry(path);
                    bitmap = cached;
                    return true;
                }
            }

            bitmap = null;
            return false;
        }

        private static void AddToMemoryStrongCache(string path, BitmapImage bitmap)
        {
            lock (MemoryStrongCacheLock)
            {
                MemoryStrongCache[path] = bitmap;
                TouchMemoryStrongCacheEntry(path);

                while (MemoryStrongCache.Count > MaxStrongCacheEntries && MemoryStrongCacheLru.Last is LinkedListNode<string> tailNode)
                {
                    string evictedPath = tailNode.Value;
                    MemoryStrongCacheLru.RemoveLast();
                    MemoryStrongCacheNodes.Remove(evictedPath);
                    MemoryStrongCache.Remove(evictedPath);
                }
            }
        }

        private static void TouchMemoryStrongCacheEntry(string path)
        {
            if (MemoryStrongCacheNodes.TryGetValue(path, out var existingNode))
            {
                MemoryStrongCacheLru.Remove(existingNode);
            }
            else
            {
                existingNode = new LinkedListNode<string>(path);
                MemoryStrongCacheNodes[path] = existingNode;
            }

            MemoryStrongCacheLru.AddFirst(existingNode);
        }

        private static void RemoveFromMemoryStrongCache(string path)
        {
            lock (MemoryStrongCacheLock)
            {
                if (MemoryStrongCacheNodes.TryGetValue(path, out var node))
                {
                    MemoryStrongCacheLru.Remove(node);
                    MemoryStrongCacheNodes.Remove(path);
                }

                MemoryStrongCache.Remove(path);
            }
        }

        private static void ClearMemoryStrongCache()
        {
            lock (MemoryStrongCacheLock)
            {
                MemoryStrongCache.Clear();
                MemoryStrongCacheLru.Clear();
                MemoryStrongCacheNodes.Clear();
            }
        }

        private static void TryPinViewportPath(string path, BitmapImage bitmap)
        {
            lock (ViewportStrongCacheLock)
            {
                if (DesiredViewportPaths.Contains(path))
                {
                    ViewportStrongCache[path] = bitmap;
                }
            }
        }

        private static void RemoveFromViewportStrongCache(string path)
        {
            lock (ViewportStrongCacheLock)
            {
                ViewportStrongCache.Remove(path);
                DesiredViewportPaths.Remove(path);
            }
        }

        private sealed class ImageLoadScope : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;

            public ImageLoadScope(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
                _semaphore.Wait();
            }

            public void Dispose() => _semaphore.Release();
        }
    }
}
