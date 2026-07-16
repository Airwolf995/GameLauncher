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
        private const int DecodePixelWidth = 256;
        private const int MaxParallelImageLoads = 2;
        private const int MaxParallelPreloadImageLoads = 1;
        private const int MaxStrongCacheEntries = 1024;
        private const long MaxStrongCacheBytes = 128L * 1024 * 1024;
        private const int PreloadLogImageCountThreshold = 10;
        private const int PreloadLogDurationThresholdMs = 250;

        private static readonly Dictionary<string, BitmapImage> MemoryStrongCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> MemoryStrongCacheLru = [];
        private static readonly Dictionary<string, LinkedListNode<string>> MemoryStrongCacheNodes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, long> MemoryStrongCacheSizes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object MemoryStrongCacheLock = new();
        private static readonly Dictionary<string, PathLoadLock> PathLoadLocks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object PathLoadLocksSync = new();
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        private static readonly SemaphoreSlim ImageLoadLimiter = new(MaxParallelImageLoads);

        private static int _strongCacheHits;
        private static int _preloadLoadCount;
        private static int _uiMissCount;
        private static long _strongCacheBytes;

        public static BitmapImage? GetCachedForUi(string path)
        {
            if (TryGetCachedBitmap(path, out var cachedBitmap))
            {
                return cachedBitmap;
            }

            Interlocked.Increment(ref _uiMissCount);
            return null;
        }

        public static async Task PreloadAsync(IEnumerable<string> imagePaths, CancellationToken ct = default)
        {
            var pathsToLoad = imagePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !IsCached(path))
                .ToList();

            if (pathsToLoad.Count == 0)
            {
                return;
            }

            ResetCacheStats();
            var watch = Stopwatch.StartNew();
            bool shouldLogStart = pathsToLoad.Count >= PreloadLogImageCountThreshold;
            if (shouldLogStart)
            {
                Logger.Log($"Image preload started: {pathsToLoad.Count} cover image(s).");
            }

            await Parallel.ForEachAsync(
                pathsToLoad,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxParallelPreloadImageLoads,
                    CancellationToken = ct
                },
                async (path, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    await LoadAndCacheBitmapAsync(path, token);
                });

            watch.Stop();
            if (shouldLogStart || watch.ElapsedMilliseconds >= PreloadLogDurationThresholdMs)
            {
                Logger.Log($"Image preload completed: {pathsToLoad.Count} cover image(s) in {watch.ElapsedMilliseconds} ms.");
            }
        }

        public static bool IsCached(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return TryGetMemoryStrongCache(path, out _);
        }

        public static void ReleaseStartupStrongCache()
        {
            Logger.Log(
                $"Image cache stats: strong hits={_strongCacheHits}, preload loads={_preloadLoadCount}, ui misses={_uiMissCount}.");
        }

        public static void Invalidate(string path)
        {
            RemoveFromMemoryStrongCache(path);
        }

        public static void Clear()
        {
            ClearMemoryStrongCache();

            Logger.Log("Image caches cleared for manual library refresh.");
        }

        public static void UpdateViewportRetention(IEnumerable<string> imagePaths)
        {
            foreach (string path in imagePaths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    TryGetMemoryStrongCache(path, out _);
                }
            }
        }

        private static async Task<BitmapImage?> LoadAndCacheBitmapAsync(
            string path,
            CancellationToken cancellationToken)
        {
            if (TryGetCachedBitmap(path, out var cachedBitmap))
            {
                return cachedBitmap;
            }

            PathLoadLock pathLock = RentPathLoadLock(path);
            try
            {
                await pathLock.Semaphore.WaitAsync(cancellationToken);
                try
                {
                    if (TryGetCachedBitmap(path, out cachedBitmap))
                    {
                        return cachedBitmap;
                    }

                    Interlocked.Increment(ref _preloadLoadCount);

                    await ImageLoadLimiter.WaitAsync(cancellationToken);
                    try
                    {
                        BitmapImage bitmap = await LoadBitmapAsync(path, cancellationToken);

                        AddToMemoryStrongCache(path, bitmap);
                        return bitmap;
                    }
                    finally
                    {
                        ImageLoadLimiter.Release();
                    }
                }
                finally
                {
                    pathLock.Semaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
            finally
            {
                ReturnPathLoadLock(path, pathLock);
            }
        }

        private static bool TryGetCachedBitmap(string path, out BitmapImage? bitmap)
        {
            if (TryGetMemoryStrongCache(path, out var strongBitmap))
            {
                Interlocked.Increment(ref _strongCacheHits);
                bitmap = strongBitmap;
                return true;
            }

            bitmap = null;
            return false;
        }

        private static async Task<BitmapImage> LoadBitmapAsync(string path, CancellationToken cancellationToken)
        {
            Uri uri = new(path);
            bool isRemote = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
            BitmapImage bitmap = isRemote
                ? await LoadRemoteBitmapAsync(uri, cancellationToken)
                : LoadLocalBitmap(uri);

#if DEBUG
            Logger.Log(
                isRemote
                    ? $"[BitmapCache] Remote Image Loaded: {path} ({bitmap.PixelWidth}x{bitmap.PixelHeight}) - Decoded & Frozen"
                    : $"[BitmapCache] Local Image Loaded: {path} ({bitmap.PixelWidth}x{bitmap.PixelHeight}) - Decoded to {DecodePixelWidth}px width");
#endif

            return bitmap;
        }

        private static async Task<BitmapImage> LoadRemoteBitmapAsync(Uri uri, CancellationToken cancellationToken)
        {
            byte[] bytes = await HttpClient.GetByteArrayAsync(uri, cancellationToken);
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
            Interlocked.Exchange(ref _strongCacheHits, 0);
            Interlocked.Exchange(ref _preloadLoadCount, 0);
            Interlocked.Exchange(ref _uiMissCount, 0);
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
                if (MemoryStrongCacheSizes.Remove(path, out long previousSize))
                {
                    _strongCacheBytes -= previousSize;
                }

                long bitmapSize = Math.Max(1L, (long)bitmap.PixelWidth * bitmap.PixelHeight * 4);
                MemoryStrongCache[path] = bitmap;
                MemoryStrongCacheSizes[path] = bitmapSize;
                _strongCacheBytes += bitmapSize;
                TouchMemoryStrongCacheEntry(path);

                while ((MemoryStrongCache.Count > MaxStrongCacheEntries || _strongCacheBytes > MaxStrongCacheBytes) &&
                       MemoryStrongCacheLru.Last is LinkedListNode<string> tailNode)
                {
                    string evictedPath = tailNode.Value;
                    MemoryStrongCacheLru.RemoveLast();
                    MemoryStrongCacheNodes.Remove(evictedPath);
                    MemoryStrongCache.Remove(evictedPath);
                    if (MemoryStrongCacheSizes.Remove(evictedPath, out long evictedSize))
                    {
                        _strongCacheBytes -= evictedSize;
                    }
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
                if (MemoryStrongCacheSizes.Remove(path, out long removedSize))
                {
                    _strongCacheBytes -= removedSize;
                }
            }
        }

        private static void ClearMemoryStrongCache()
        {
            lock (MemoryStrongCacheLock)
            {
                MemoryStrongCache.Clear();
                MemoryStrongCacheLru.Clear();
                MemoryStrongCacheNodes.Clear();
                MemoryStrongCacheSizes.Clear();
                _strongCacheBytes = 0;
            }
        }

        private static PathLoadLock RentPathLoadLock(string path)
        {
            lock (PathLoadLocksSync)
            {
                if (!PathLoadLocks.TryGetValue(path, out var pathLock))
                {
                    pathLock = new PathLoadLock();
                    PathLoadLocks[path] = pathLock;
                }

                pathLock.ReferenceCount++;
                return pathLock;
            }
        }

        private static void ReturnPathLoadLock(string path, PathLoadLock pathLock)
        {
            lock (PathLoadLocksSync)
            {
                pathLock.ReferenceCount--;
                if (pathLock.ReferenceCount == 0)
                {
                    PathLoadLocks.Remove(path);
                    pathLock.Semaphore.Dispose();
                }
            }
        }

        private sealed class PathLoadLock
        {
            public SemaphoreSlim Semaphore { get; } = new(1, 1);
            public int ReferenceCount { get; set; }
        }
    }
}
