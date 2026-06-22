using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    internal static class ExecutableSelector
    {
        public static string FindPrimaryExecutable(string installDirectory, params string[] excludedNameFragments)
        {
            try
            {
                HashSet<string> exclusions = new(excludedNameFragments, StringComparer.OrdinalIgnoreCase);
                return Directory
                    .EnumerateFiles(installDirectory, "*.exe", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(path => !exclusions.Any(fragment =>
                        Path.GetFileNameWithoutExtension(path).Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                    ?? string.Empty;
            }
            catch (Exception ex)
            {
                Logger.Error($"Executable search failed in {installDirectory}", ex);
                return string.Empty;
            }
        }
    }
}
