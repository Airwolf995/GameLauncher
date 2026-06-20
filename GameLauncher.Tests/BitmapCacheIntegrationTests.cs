using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using GameLauncher.Models;

namespace GameLauncher.Tests
{
    public class BitmapCacheIntegrationTests
    {
        [Fact]
        public void Convert_LoadsLocalImageAsFrozenBitmap()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "GameLauncherTests", Guid.NewGuid().ToString("N"));
            var imagePath = Path.Combine(tempRoot, "cover.png");

            Directory.CreateDirectory(tempRoot);
            File.WriteAllBytes(imagePath, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));

            try
            {
                var converter = new BitmapCacheConverter();

                var result = converter.Convert(imagePath, typeof(BitmapImage), null!, System.Globalization.CultureInfo.InvariantCulture);

                var bitmap = Assert.IsType<BitmapImage>(result);
                Assert.True(bitmap.IsFrozen);
                Assert.True(bitmap.PixelWidth > 0);
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
        public void SetManualGameImage_InvalidatesBitmapCacheForTargetPath()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "GameLauncherTests", Guid.NewGuid().ToString("N"));
            var configPath = Path.Combine(tempRoot, "game_launcher_config.json");
            var sourceImagePath = Path.Combine(tempRoot, "cover.png");

            // The service stores the image in an "images/" subfolder next to the config file
            var imagesDir = Path.Combine(tempRoot, "images");
            // The destination filename is derived from the game name (safe chars) + extension
            var targetPath = Path.Combine(imagesDir, "Testspiel.png");

            Directory.CreateDirectory(tempRoot);
            File.WriteAllBytes(sourceImagePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // minimal file

            var cacheField = typeof(BitmapCacheConverter).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(cacheField);

            var cache = Assert.IsType<ConcurrentDictionary<string, WeakReference<BitmapImage>>>(cacheField!.GetValue(null));
            cache.Clear();
            // Pre-populate cache with the expected destination path so we can verify invalidation
            cache[targetPath] = new WeakReference<BitmapImage>(new BitmapImage());

            try
            {
                var manager = new GameManager(configPath);
                var game = new Game { Id = "manual_test_game", Name = "Testspiel", IsManual = true };
                manager.Config.ManualGames.Add(game);

                manager.SetManualGameImage(game, sourceImagePath);

                Assert.False(cache.ContainsKey(targetPath));
            }
            finally
            {
                cache.Clear();

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
    }
}
