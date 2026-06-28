using System;
using System.IO;
using System.Windows.Media.Imaging;
using GameLauncher.Models;

namespace GameLauncher.Tests
{
    public class BitmapCacheIntegrationTests
    {
        [Fact]
        public async Task Convert_ReturnsPreloadedLocalImageAsFrozenBitmap()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "GameLauncherTests", Guid.NewGuid().ToString("N"));
            var imagePath = Path.Combine(tempRoot, "cover.png");

            Directory.CreateDirectory(tempRoot);
            File.WriteAllBytes(imagePath, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));

            try
            {
                var converter = new BitmapCacheConverter();
                await BitmapCacheConverter.PreloadAsync([imagePath]);

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
            BitmapCacheConverter.ClearImageCaches();

            var converter = new BitmapCacheConverter();
            await BitmapCacheConverter.PreloadAsync([targetPath]);
            var cachedBitmap = converter.Convert(
                targetPath,
                typeof(BitmapImage),
                null!,
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.NotNull(cachedBitmap);
            Assert.True(BitmapCacheConverter.IsCachedForUi(targetPath));

            try
            {
                var manager = new GameManager(configPath);
                var game = new Game { Id = "manual_test_game", Name = "Testspiel", IsManual = true };
                manager.Config.ManualGames.Add(game);

                manager.SetManualGameImage(game, sourceImagePath);

                Assert.False(BitmapCacheConverter.IsCachedForUi(targetPath));
            }
            finally
            {
                BitmapCacheConverter.ClearImageCaches();

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
