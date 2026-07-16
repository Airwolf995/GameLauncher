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

                CleanupImageIfUnused(game.Id, oldImageUrl);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting image for '{game.Name}'", ex);
            }
        }

        public void CleanupImageIfUnused(string gameId, string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;

            try
            {
                bool isShared = Config.ManualGames.Any(g => g.Id != gameId && string.Equals(g.ImageUrl, imagePath, StringComparison.OrdinalIgnoreCase)) ||
                                Config.ImageOverrides.Any(kvp => kvp.Key != gameId && string.Equals(kvp.Value, imagePath, StringComparison.OrdinalIgnoreCase));

                if (!isShared && IsManagedImagePath(imagePath))
                {
                    BitmapCacheConverter.Invalidate(imagePath);
                    File.Delete(imagePath);
                    Logger.Log($"Nicht mehr verwendetes Bild gelöscht: {imagePath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Nicht mehr verwendetes Bild konnte nicht bereinigt werden: {imagePath}", ex);
            }
        }

        private bool IsManagedImagePath(string imagePath)
        {
            string configDir = Path.GetDirectoryName(_configService.ConfigPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string[] managedDirectories =
            {
                AppPaths.GetDownloadedCoversDirectory(),
                AppPaths.GetExtractedIconsDirectory(),
                Path.Combine(configDir, "images")
            };

            return managedDirectories.Any(directory => IsPathInsideDirectory(imagePath, directory));
        }

        private static bool IsPathInsideDirectory(string filePath, string directoryPath)
        {
            string fullFilePath = Path.GetFullPath(filePath);
            string fullDirectoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
            string relativePath = Path.GetRelativePath(fullDirectoryPath, fullFilePath);

            return !Path.IsPathRooted(relativePath) &&
                   !string.Equals(relativePath, "..", StringComparison.Ordinal) &&
                   !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                   !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
        }
    }
}
