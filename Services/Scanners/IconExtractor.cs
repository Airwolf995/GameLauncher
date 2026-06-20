using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Utility class for extracting icons from executable files.
    /// </summary>
    public static class IconExtractor
    {
        /// <summary>
        /// Extracts the icon from an executable file and saves it as PNG.
        /// </summary>
        /// <param name="exePath">Path to the executable.</param>
        /// <param name="gameId">Game ID used for the cache filename.</param>
        /// <returns>Path to the cached icon, or empty string if extraction failed.</returns>
        public static string GetIconFromExe(string exePath, string gameId)
        {
            try
            {
                if (!File.Exists(exePath)) return "";

                string cacheDir = Services.AppPaths.GetExtractedIconsDirectory();
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

                // Sanitize ID for filename
                string safeId = string.Join("_", gameId.Split(Path.GetInvalidFileNameChars()));
                string iconPath = Path.Combine(cacheDir, $"{safeId}.png");

                if (File.Exists(iconPath)) return iconPath;

                // Extract Icon
                using (var icon = Icon.ExtractAssociatedIcon(exePath))
                {
                    if (icon != null)
                    {
                        using (var bitmap = icon.ToBitmap())
                        {
                            bitmap.Save(iconPath, ImageFormat.Png);
                            return iconPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to extract icon from {exePath}", ex);
            }
            return "";
        }
    }
}
