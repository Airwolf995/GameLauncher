using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using GameLauncher.Models;

namespace GameLauncher.Services
{
    internal enum GameImageLoadStatus
    {
        Success,
        TemporaryFailure,
        NotFound
    }

    internal readonly record struct GameImageLoadResult(
        BitmapImage? Bitmap,
        GameImageLoadStatus Status,
        TimeSpan RetryDelay)
    {
        public bool ShouldRetryAutomatically => Status == GameImageLoadStatus.TemporaryFailure;
    }

    internal static class GameImageBitmapCache
    {
        private const int DecodePixelWidth = 256;
        private const int MaxParallelImageLoads = 2;
        private const int MaxStrongCacheEntries = 1024;
        private const long MaxStrongCacheBytes = 128L * 1024 * 1024;
        private static readonly TimeSpan TemporaryFailureRetryDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MissingRemoteImageRetryDelay = TimeSpan.FromHours(24);

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
        private static readonly ConcurrentDictionary<string, FailedImageState> FailedImages =
            new(StringComparer.OrdinalIgnoreCase);

        private static long _strongCacheBytes;

        public static Task<GameImageLoadResult> LoadAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Task.FromResult(new GameImageLoadResult(
                    null,
                    GameImageLoadStatus.NotFound,
                    TimeSpan.Zero));
            }

            return LoadAndCacheBitmapAsync(path, cancellationToken);
        }

        public static bool IsCached(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return TryGetMemoryStrongCache(path, out _);
        }

        public static void Invalidate(string path)
        {
            RemoveFromMemoryStrongCache(path);
            FailedImages.TryRemove(path, out _);
        }

        public static void Clear()
        {
            ClearMemoryStrongCache();
            FailedImages.Clear();

            Logger.Log("Image caches cleared for manual library refresh.");
        }

        private static async Task<GameImageLoadResult> LoadAndCacheBitmapAsync(
            string path,
            CancellationToken cancellationToken)
        {
            if (TryGetCachedBitmap(path, out var cachedBitmap))
            {
                return CreateSuccessResult(cachedBitmap!);
            }

            PathLoadLock pathLock = RentPathLoadLock(path);
            try
            {
                await pathLock.Semaphore.WaitAsync(cancellationToken);
                try
                {
                    if (TryGetCachedBitmap(path, out cachedBitmap))
                    {
                        return CreateSuccessResult(cachedBitmap!);
                    }

                    // Ein paralleler Ladevorgang kann denselben Pfad bereits erfolglos
                    // verarbeitet haben, während dieser Aufruf auf die Pfadsperre wartete.
                    if (TryGetActiveFailure(path, out FailedImageState failedImage))
                    {
                        return CreateFailureResult(failedImage);
                    }

                    await ImageLoadLimiter.WaitAsync(cancellationToken);
                    try
                    {
                        try
                        {
                            BitmapImage bitmap = await LoadBitmapAsync(path, cancellationToken);

                            AddToMemoryStrongCache(path, bitmap);
                            FailedImages.TryRemove(path, out _);
                            return CreateSuccessResult(bitmap);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            return CreateFailureResult(RegisterLoadFailure(path, ex));
                        }
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
            catch (Exception ex)
            {
                return CreateFailureResult(RegisterLoadFailure(path, ex));
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

        private static bool TryGetActiveFailure(string path, out FailedImageState failedImage)
        {
            if (!FailedImages.TryGetValue(path, out failedImage))
            {
                return false;
            }

            if (failedImage.RetryAfterUtc > DateTime.UtcNow)
            {
                return true;
            }

            FailedImages.TryRemove(path, out _);
            failedImage = default;
            return false;
        }

        private static FailedImageState RegisterLoadFailure(string path, Exception exception)
        {
            GameImageLoadStatus status = GetFailureStatus(exception);
            var failedImage = new FailedImageState(
                DateTime.UtcNow.Add(GetFailureRetryDelay(exception)),
                status);
            FailedImages[path] = failedImage;

            if (exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound })
            {
                Logger.Warning($"Cover nicht verfügbar (HTTP 404): {path}");
                return failedImage;
            }

            Logger.Error($"Bild konnte nicht geladen werden: {path}", exception);
            return failedImage;
        }

        private static GameImageLoadResult CreateSuccessResult(BitmapImage bitmap) =>
            new(bitmap, GameImageLoadStatus.Success, TimeSpan.Zero);

        private static GameImageLoadResult CreateFailureResult(FailedImageState failedImage)
        {
            DateTime utcNow = DateTime.UtcNow;
            return new GameImageLoadResult(
                null,
                failedImage.Status,
                GetRemainingRetryDelay(failedImage.RetryAfterUtc, utcNow));
        }

        internal static TimeSpan GetRemainingRetryDelay(DateTime retryAfterUtc, DateTime utcNow)
        {
            TimeSpan retryDelay = retryAfterUtc - utcNow;
            return retryDelay > TimeSpan.Zero ? retryDelay : TimeSpan.Zero;
        }

        internal static GameImageLoadStatus GetFailureStatus(Exception exception) =>
            exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
                ? GameImageLoadStatus.NotFound
                : GameImageLoadStatus.TemporaryFailure;

        internal static TimeSpan GetFailureRetryDelay(Exception exception) =>
            exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
                ? MissingRemoteImageRetryDelay
                : TemporaryFailureRetryDelay;

        private readonly record struct FailedImageState(
            DateTime RetryAfterUtc,
            GameImageLoadStatus Status);

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
