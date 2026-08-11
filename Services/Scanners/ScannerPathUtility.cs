using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    internal static class ScannerPathUtility
    {
        public static void AddExistingDirectory(ICollection<string> paths, string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            string normalizedPath = Normalize(path);
            if (!paths.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(normalizedPath);
            }
        }

        public static List<string> NormalizeDistinct(IEnumerable<string>? paths) =>
            paths?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(TryNormalize)
                .Where(result => result.Success)
                .Select(result => result.Path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            ?? [];

        public static List<string> GetLibraryDirectories(IEnumerable<string> installDirectories) =>
            NormalizeDistinct(installDirectories)
                .Select(path => Directory.GetParent(path)?.FullName ?? path)
                .Select(TryNormalize)
                .Where(result => result.Success)
                .Select(result => result.Path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

        public static bool TryNormalize(string? path, out string normalizedPath)
        {
            normalizedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                normalizedPath = Normalize(path);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Logger.Log($"Ungültiger Bibliothekspfad wird übersprungen: {ex.GetType().Name}");
                return false;
            }
        }

        private static (bool Success, string? Path) TryNormalize(string path)
        {
            return TryNormalize(path, out var normalizedPath)
                ? (true, normalizedPath)
                : (false, null);
        }

        public static string Normalize(string path)
        {
            string fullPath = Path.GetFullPath(path.Trim());
            string? rootPath = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(rootPath) &&
                string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return rootPath;
            }

            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
