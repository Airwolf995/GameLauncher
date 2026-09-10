using System.Collections.Generic;
using System.Linq;
using System.Windows.Documents;
using GameLauncher.Services;

namespace GameLauncher.Tests
{
    public class ChangelogFormatterTests
    {
        private const double BaseFontSize = 13;

        [Fact]
        public void CreateInlines_RendersHeadingAsBoldWithoutHashes()
        {
            IReadOnlyList<Inline> inlines = ChangelogFormatter.CreateInlines("### Behoben", BaseFontSize);

            Bold heading = Assert.IsType<Bold>(Assert.Single(inlines));
            Assert.Equal("Behoben", TextOf(heading));
            Assert.Equal(BaseFontSize + 2, heading.FontSize);
        }

        [Theory]
        [InlineData("# Titel")]
        [InlineData("###### Sehr tief")]
        [InlineData("## Mit Abschluss ##")]
        public void CreateInlines_RemovesHashesForAllHeadingLevels(string line)
        {
            IReadOnlyList<Inline> inlines = ChangelogFormatter.CreateInlines(line, BaseFontSize);

            Assert.DoesNotContain("#", string.Concat(inlines.Select(TextOf)));
        }

        [Fact]
        public void CreateInlines_ConvertsDashListToBulletAndKeepsBoldText()
        {
            IReadOnlyList<Inline> inlines = ChangelogFormatter.CreateInlines(
                "- **Dauerhafte Prozessorlast**: behoben",
                BaseFontSize);

            Assert.Equal("• ", TextOf(inlines[0]));
            Assert.IsType<Bold>(inlines[1]);
            Assert.Equal("Dauerhafte Prozessorlast", TextOf(inlines[1]));
            Assert.Equal(": behoben", TextOf(inlines[2]));
        }

        [Fact]
        public void CreateInlines_AddsBlankLineBeforeHeadingThatFollowsText()
        {
            IReadOnlyList<Inline> inlines = ChangelogFormatter.CreateInlines("Text\n## Behoben", BaseFontSize);

            // Zeilenumbruch der Textzeile plus zusätzlicher Abstand vor der Überschrift.
            Assert.Equal(2, inlines.OfType<LineBreak>().Count());
            Assert.IsType<Bold>(inlines.Last());
        }

        [Fact]
        public void CreateInlines_DoesNotAddSpacingBeforeLeadingHeading()
        {
            IReadOnlyList<Inline> inlines = ChangelogFormatter.CreateInlines("## Neu\nText", BaseFontSize);

            Assert.IsType<Bold>(inlines[0]);
        }

        [Fact]
        public void CreateInlines_CreatesHyperlinkForMarkdownLink()
        {
            IReadOnlyList<Inline> inlines = ChangelogFormatter.CreateInlines(
                "Siehe [Release](https://example.com/r)",
                BaseFontSize);

            Hyperlink link = Assert.IsType<Hyperlink>(inlines[1]);
            Assert.Equal("Release", TextOf(link));
            Assert.Equal("https://example.com/r", link.NavigateUri.AbsoluteUri);
        }

        [Fact]
        public void CreateInlines_DoesNotDoubleSpacingWhenBlankLinePrecedesHeading()
        {
            IReadOnlyList<Inline> inlines = ChangelogFormatter.CreateInlines("Text\n\n## Behoben", BaseFontSize);

            // Nur die beiden Umbrüche der Leerzeile, kein zusätzlicher Abstand.
            Assert.Equal(2, inlines.OfType<LineBreak>().Count());
            Assert.IsType<Bold>(inlines.Last());
        }

        [Fact]
        public void CreateInlines_KeepsPlainTextUnchanged()
        {
            IReadOnlyList<Inline> inlines = ChangelogFormatter.CreateInlines("Nur Text", BaseFontSize);

            Assert.Equal("Nur Text", TextOf(Assert.Single(inlines)));
        }

        private static string TextOf(Inline inline) => inline switch
        {
            Run run => run.Text,
            LineBreak => "\n",
            Span span => string.Concat(span.Inlines.Select(TextOf)),
            _ => string.Empty
        };
    }
}
