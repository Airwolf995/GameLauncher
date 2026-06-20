using System;
using System.IO;
using System.Linq;
using GameLauncher.Models;

namespace GameLauncher.Services
{
    /// <summary>
    /// Handles game cover image management.
    /// Extracted from GameManager to follow Single Responsibility Principle.
    /// </summary>
    public class GameImageService
    {
        private readonly ConfigService _configService;

        public GameImageService(ConfigService configService)
        {
            _configService = configService;
        }

        private GameConfig Config => _configService.Config;

        /// <summary>
        /// Sets a custom cover image for a manual game.
        /// Copies the image to the app's images folder and updates the config.
        /// </summary>
        public void SetManualGameImage(Game game, string imagePath)
        {
            try
            {
                // Create images folder next to config
                string configDir = Path.GetDirectoryName(_configService.ConfigPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                string imagesDir = Path.Combine(configDir, "images");
                if (!Directory.Exists(imagesDir))
                {
                    Directory.CreateDirectory(imagesDir);
                }

                // Clean filename
                string safeGameName = string.Join("_", game.Name.Split(Path.GetInvalidFileNameChars()));
                string extension = Path.GetExtension(imagePath);
                string destFileName = $"{safeGameName}{extension}";
                string destPath = Path.Combine(imagesDir, destFileName);

                string oldImageUrl = game.ImageUrl;

                // Invalidate bitmap cache for old and new path
                BitmapCacheConverter.Invalidate(game.ImageUrl);
                BitmapCacheConverter.Invalidate(destPath);

                // Copy image
                File.Copy(imagePath, destPath, true);

                // Update game
                game.ImageUrl = destPath;

                // Store override in config
                Config.ImageOverrides[game.Id] = destPath;
                _configService.SaveConfig();

                Logger.Log($"Set custom image for '{game.Name}': {destPath}");

                CleanupOldImage(game.Id, oldImageUrl);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting image for '{game.Name}'", ex);
            }
        }

        private void CleanupOldImage(string gameId, string oldImageUrl)
        {
            if (string.IsNullOrEmpty(oldImageUrl) || !File.Exists(oldImageUrl)) return;

            try
            {
                bool isShared = Config.ManualGames.Any(g => g.Id != gameId && string.Equals(g.ImageUrl, oldImageUrl, StringComparison.OrdinalIgnoreCase)) ||
                                (Config.ImageOverrides != null && Config.ImageOverrides.Any(kvp => kvp.Key != gameId && string.Equals(kvp.Value, oldImageUrl, StringComparison.OrdinalIgnoreCase)));

                if (!isShared)
                {
                    string downloadedCoversDir = AppPaths.GetDownloadedCoversDirectory();
                    string extractedIconsDir = AppPaths.GetExtractedIconsDirectory();
                    string configDir = Path.GetDirectoryName(_configService.ConfigPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    string imagesDir = Path.Combine(configDir, "images");

                    string fullOldPath = Path.GetFullPath(oldImageUrl);

                    if (fullOldPath.StartsWith(Path.GetFullPath(downloadedCoversDir), StringComparison.OrdinalIgnoreCase) ||
                        fullOldPath.StartsWith(Path.GetFullPath(extractedIconsDir), StringComparison.OrdinalIgnoreCase) ||
                        fullOldPath.StartsWith(Path.GetFullPath(imagesDir), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(oldImageUrl);
                        Logger.Log($"Deleted unused old image: {oldImageUrl}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to cleanup old image: {oldImageUrl}", ex);
            }
        }
    }
}
