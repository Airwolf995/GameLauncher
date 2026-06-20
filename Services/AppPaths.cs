using System;
using System.IO;
using System.Linq;
using GameLauncher.Models;

namespace GameLauncher.Services
{
    public static class AppPaths
    {
        public static string GetDocumentsRoot()
        {
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documentsPath, "GameLauncher");
        }

        public static string GetLegacyCacheDirectory() =>
            Path.Combine(GetDocumentsRoot(), "Cache");

        public static string GetArtworkRoot() =>
            Path.Combine(GetDocumentsRoot(), "Artwork");

        public static string GetDownloadedCoversDirectory() =>
            Path.Combine(GetArtworkRoot(), "DownloadedCovers");

        public static string GetExtractedIconsDirectory() =>
            Path.Combine(GetArtworkRoot(), "ExtractedIcons");

        public static bool MigrateLegacyArtwork(GameConfig config)
        {
            string legacyCacheDir = GetLegacyCacheDirectory();
            if (!Directory.Exists(legacyCacheDir))
            {
                return false;
            }

            bool changed = false;

            Directory.CreateDirectory(GetDownloadedCoversDirectory());
            Directory.CreateDirectory(GetExtractedIconsDirectory());

            foreach (var file in Directory.EnumerateFiles(legacyCacheDir))
            {
                string targetPath = GetArtworkTargetPath(file);
                if (string.Equals(file, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!File.Exists(targetPath))
                {
                    File.Move(file, targetPath);
                    changed = true;
                }
                else if (File.Exists(file))
                {
                    File.Delete(file);
                    changed = true;
                }
            }

            foreach (var game in config.ManualGames)
            {
                string migratedPath = TryGetMigratedPath(game.ImageUrl);
                if (!string.Equals(game.ImageUrl, migratedPath, StringComparison.OrdinalIgnoreCase))
                {
                    game.ImageUrl = migratedPath;
                    changed = true;
                }
            }

            foreach (var key in config.ImageOverrides.Keys.ToList())
            {
                string migratedPath = TryGetMigratedPath(config.ImageOverrides[key]);
                if (!string.Equals(config.ImageOverrides[key], migratedPath, StringComparison.OrdinalIgnoreCase))
                {
                    config.ImageOverrides[key] = migratedPath;
                    changed = true;
                }
            }

            return changed;
        }

        private static string TryGetMigratedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            string legacyCacheDir = Path.GetFullPath(GetLegacyCacheDirectory());
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(legacyCacheDir, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            string targetPath = GetArtworkTargetPath(fullPath);
            return File.Exists(targetPath) ? targetPath : path;
        }

        private static string GetArtworkTargetPath(string sourcePath)
        {
            string fileName = Path.GetFileName(sourcePath);
            string targetDirectory = fileName.Contains("_cover_", StringComparison.OrdinalIgnoreCase)
                ? GetDownloadedCoversDirectory()
                : GetExtractedIconsDirectory();
            return Path.Combine(targetDirectory, fileName);
        }
    }
}
