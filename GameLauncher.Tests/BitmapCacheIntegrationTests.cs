using System;
using System.IO;
using System.Net;
using System.Net.Http;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.GameManagement;

namespace GameLauncher.Tests
{
    public class BitmapCacheIntegrationTests
    {
        [Fact]
        public async Task LoadAsync_ReturnsLocalImageAsFrozenBitmap()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "GameLauncherTests", Guid.NewGuid().ToString("N"));
            var imagePath = Path.Combine(tempRoot, "cover.png");

            Directory.CreateDirectory(tempRoot);
            File.WriteAllBytes(imagePath, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));

            try
            {
                GameImageLoadResult result = await GameImageBitmapCache.LoadAsync(imagePath);

                Assert.Equal(GameImageLoadStatus.Success, result.Status);
                Assert.NotNull(result.Bitmap);
                Assert.True(result.Bitmap.IsFrozen);
                Assert.True(result.Bitmap.PixelWidth > 0);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                }
            }
        }

        [Fact]
        public async Task SetManualGameImage_InvalidatesBitmapCacheForTargetPath()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "GameLauncherTests", Guid.NewGuid().ToString("N"));
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");
            var sourceImagePath = Path.Combine(tempRoot, "cover.png");
            var pngBytes = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

            // The service stores the image in an "images/" subfolder next to the config file
            var imagesDir = Path.Combine(tempRoot, "images");
            // The destination filename is derived from the game name (safe chars) + extension
            var targetPath = Path.Combine(imagesDir, "Testspiel.png");

            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(imagesDir);
            File.WriteAllBytes(sourceImagePath, pngBytes);
            File.WriteAllBytes(targetPath, pngBytes);
            GameImageBitmapCache.Clear();

            GameImageLoadResult cachedResult = await GameImageBitmapCache.LoadAsync(targetPath);
            Assert.NotNull(cachedResult.Bitmap);
            Assert.True(GameImageBitmapCache.IsCached(targetPath));

            try
            {
                using var manager = new GameManager(configPath);
                var game = new Game { Id = "manual_test_game", Name = "Testspiel", IsManual = true };
                manager.UpdateConfig(config => config.ManualGames.Add(game));

                manager.SetManualGameImage(game, sourceImagePath);

                Assert.False(GameImageBitmapCache.IsCached(targetPath));
                Assert.Equal(targetPath, game.ImageUrl);
                Assert.Equal(targetPath, manager.Config.ImageOverrides[game.Id]);
            }
            finally
            {
                GameImageBitmapCache.Clear();

                try
                {
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                }
                catch
                {
                }

                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                }
            }
        }

        [Fact]
        public async Task FailedLoad_IsSkippedUntilInvalidated()
        {
            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "GameLauncherTests",
                Guid.NewGuid().ToString("N"));
            var missingImagePath = Path.Combine(tempRoot, "missing-cover.png");
            var pngBytes = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

            GameImageBitmapCache.Clear();

            try
            {
                GameImageLoadResult missingResult = await GameImageBitmapCache.LoadAsync(missingImagePath);

                Assert.Null(missingResult.Bitmap);
                Assert.Equal(GameImageLoadStatus.TemporaryFailure, missingResult.Status);
                Assert.True(missingResult.ShouldRetryAutomatically);
                Assert.False(GameImageBitmapCache.IsCached(missingImagePath));

                Directory.CreateDirectory(tempRoot);
                File.WriteAllBytes(missingImagePath, pngBytes);
                GameImageLoadResult retryResult = await GameImageBitmapCache.LoadAsync(missingImagePath);
                Assert.Null(retryResult.Bitmap);
                Assert.Equal(GameImageLoadStatus.TemporaryFailure, retryResult.Status);
                Assert.False(GameImageBitmapCache.IsCached(missingImagePath));

                GameImageBitmapCache.Invalidate(missingImagePath);

                GameImageLoadResult loadedResult = await GameImageBitmapCache.LoadAsync(missingImagePath);
                Assert.NotNull(loadedResult.Bitmap);
                Assert.Equal(GameImageLoadStatus.Success, loadedResult.Status);
                Assert.True(GameImageBitmapCache.IsCached(missingImagePath));
            }
            finally
            {
                GameImageBitmapCache.Clear();

                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                }
            }
        }

        [Fact]
        public void GetFailureRetryDelay_DistinguishesMissingRemoteImagesFromTemporaryFailures()
        {
            var notFound = new HttpRequestException(
                "Nicht gefunden",
                inner: null,
                HttpStatusCode.NotFound);

            Assert.Equal(TimeSpan.FromHours(24), GameImageBitmapCache.GetFailureRetryDelay(notFound));
            Assert.Equal(GameImageLoadStatus.NotFound, GameImageBitmapCache.GetFailureStatus(notFound));
            Assert.Equal(
                TimeSpan.FromSeconds(30),
                GameImageBitmapCache.GetFailureRetryDelay(new HttpRequestException("Netzwerkfehler")));
            Assert.Equal(
                GameImageLoadStatus.TemporaryFailure,
                GameImageBitmapCache.GetFailureStatus(new HttpRequestException("Netzwerkfehler")));
        }

        [Fact]
        public void GetRemainingRetryDelay_ClampsExpiredFailureToZero()
        {
            DateTime utcNow = new(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);

            TimeSpan retryDelay = GameImageBitmapCache.GetRemainingRetryDelay(
                utcNow.Subtract(TimeSpan.FromMilliseconds(1)),
                utcNow);

            Assert.Equal(TimeSpan.Zero, retryDelay);
        }
    }
}
