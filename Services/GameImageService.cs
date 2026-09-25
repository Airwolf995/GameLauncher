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

                // Dateiname aus der Id statt aus dem Namen: Gleichnamige Spiele
                // verschiedener Plattformen überschrieben sonst gegenseitig ihr Cover.
                string safeId = string.Join("_", game.Id.Split(Path.GetInvalidFileNameChars()));
                string extension = Path.GetExtension(imagePath);
                string destFileName = $"{safeId}{extension}";
                string destPath = Path.Combine(imagesDir, destFileName);

                // Invalidate bitmap cache before the file is overwritten
                GameImageBitmapCache.Invalidate(destPath);
                File.Copy(imagePath, destPath, true);

                ApplyImage(game, destPath);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting image for '{game.Name}'", ex);
            }
        }

        /// <summary>
        /// Übernimmt ein heruntergeladenes Cover ohne Kopie. Es liegt bereits im
        /// verwalteten Ordner DownloadedCovers; eine Kopie ließe die
        /// heruntergeladene Datei unbenutzt zurück.
        /// </summary>
        public void SetDownloadedGameImage(Game game, string downloadedImagePath)
        {
            try
            {
                ApplyImage(game, downloadedImagePath);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting image for '{game.Name}'", ex);
            }
        }

        private void ApplyImage(Game game, string imagePath)
        {
            string oldImageUrl = game.ImageUrl;
            GameImageBitmapCache.Invalidate(oldImageUrl);

            // Spiel und Override bilden einen gemeinsamen persistierten Zustand.
            _configService.UpdateConfig(config =>
            {
                game.ImageUrl = imagePath;
                config.ImageOverrides[game.Id] = imagePath;
            });
            _configService.SaveConfig();

            Logger.Log($"Set custom image for '{game.Name}': {imagePath}");

            // Ist das neue Bild dieselbe Datei wie das alte (gleiche Endung bei
            // "Bild ändern", gleiches Cover bei der Suche), löschte die
            // Bereinigung sonst das gerade gesetzte Bild.
            if (!IsSameFile(oldImageUrl, imagePath))
            {
                CleanupImageIfUnused(game.Id, oldImageUrl);
            }
        }

        private static bool IsSameFile(string first, string second) =>
            !string.IsNullOrEmpty(first) &&
            string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

        public void CleanupImageIfUnused(string gameId, string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;

            try
            {
                bool isShared = _configService.ReadConfig(config =>
                    config.ManualGames.Any(g => g.Id != gameId && string.Equals(g.ImageUrl, imagePath, StringComparison.OrdinalIgnoreCase)) ||
                    config.ImageOverrides.Any(kvp => kvp.Key != gameId && string.Equals(kvp.Value, imagePath, StringComparison.OrdinalIgnoreCase)));

                if (!isShared && IsManagedImagePath(imagePath))
                {
                    GameImageBitmapCache.Invalidate(imagePath);
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
