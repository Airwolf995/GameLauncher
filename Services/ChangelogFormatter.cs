using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace GameLauncher.Services
{
    /// <summary>
    /// Wandelt die Markdown-Versionshinweise eines Releases in WPF-Inlines um.
    /// Bewusst ohne Fensterbezug, damit die Formatierung ohne WPF-Fenster testbar bleibt.
    /// </summary>
    public static class ChangelogFormatter
    {
        private static readonly Regex MarkdownPattern = new(
            @"\*\*(?<bold>.+?)\*\*|\[(?<linkText>[^\]]+)\]\((?<url>https?://[^)\s]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex HeadingPattern = new(
            @"^\s*(?<level>#{1,6})\s+(?<text>.+?)\s*#*\s*$",
            RegexOptions.Compiled);

        private static readonly Regex BulletPattern = new(
            @"^\s*[-*+]\s+(?<text>.*)$",
            RegexOptions.Compiled);

        public static IReadOnlyList<Inline> CreateInlines(string changelog, double baseFontSize)
        {
            var inlines = new List<Inline>();

            if (changelog == null)
            {
                return inlines;
            }

            string[] lines = changelog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                // Vor einer Überschrift soll genau eine Leerzeile stehen. Enthält der Text
                // bereits eine, darf kein weiterer Umbruch dazukommen.
                bool needsSpacingAbove = lineIndex > 0 && !string.IsNullOrWhiteSpace(lines[lineIndex - 1]);

                AddLine(inlines, lines[lineIndex], needsSpacingAbove, baseFontSize);

                if (lineIndex < lines.Length - 1)
                {
                    inlines.Add(new LineBreak());
                }
            }

            return inlines;
        }

        private static void AddLine(List<Inline> inlines, string line, bool allowSpacingAbove, double baseFontSize)
        {
            Match headingMatch = HeadingPattern.Match(line);

            if (headingMatch.Success)
            {
                if (allowSpacingAbove)
                {
                    inlines.Add(new LineBreak());
                }

                inlines.Add(new Bold(new Run(headingMatch.Groups["text"].Value))
                {
                    FontSize = baseFontSize + 2
                });

                return;
            }

            Match bulletMatch = BulletPattern.Match(line);

            if (bulletMatch.Success)
            {
                inlines.Add(new Run("• "));
                line = bulletMatch.Groups["text"].Value;
            }

            AddFormattedSegments(inlines, line);
        }

        private static void AddFormattedSegments(List<Inline> inlines, string line)
        {
            int currentIndex = 0;

            foreach (Match segment in MarkdownPattern.Matches(line))
            {
                if (segment.Index > currentIndex)
                {
                    inlines.Add(new Run(line[currentIndex..segment.Index]));
                }

                if (segment.Groups["bold"].Success)
                {
                    inlines.Add(new Bold(new Run(segment.Groups["bold"].Value)));
                }
                else if (Uri.TryCreate(segment.Groups["url"].Value, UriKind.Absolute, out Uri? uri) &&
                         (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    inlines.Add(new Hyperlink(new Run(segment.Groups["linkText"].Value))
                    {
                        NavigateUri = uri,
                        ToolTip = uri.AbsoluteUri
                    });
                }
                else
                {
                    inlines.Add(new Run(segment.Value));
                }

                currentIndex = segment.Index + segment.Length;
            }

            if (currentIndex < line.Length)
            {
                inlines.Add(new Run(line[currentIndex..]));
            }
        }
    }
}
