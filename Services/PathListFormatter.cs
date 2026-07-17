using System;
using System.Collections.Generic;
using System.Linq;

namespace GameLauncher.Services
{
    internal static class PathListFormatter
    {
        public static List<string> ParseLines(string value) =>
            value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                 .Select(line => line.Trim())
                 .Where(line => !string.IsNullOrWhiteSpace(line))
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .ToList();

        public static string FormatLines(IEnumerable<string> paths) =>
            string.Join(Environment.NewLine, paths);
    }
}
