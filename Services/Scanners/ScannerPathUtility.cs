using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
                .Select(Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            ?? [];

        public static List<string> GetLibraryDirectories(IEnumerable<string> installDirectories) =>
            installDirectories
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Normalize)
                .Select(path => Directory.GetParent(path)?.FullName ?? path)
                .Select(Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

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
