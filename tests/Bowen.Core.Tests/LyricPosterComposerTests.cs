using Bowen.Core.Models.Lyrics;
using Xunit;

namespace Bowen.Core.Tests;

public sealed class LyricPosterComposerTests
{
    private static LyricLine Line(string text, double seconds, bool translation = false) =>
        new(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1), text, []) { IsTranslation = translation };

    [Fact]
    public void OriginalAndTranslationAreIndependentItems()
    {
        var document = new LyricDocument(
            [Line("夢ならば", 1), Line("如果只是一场梦", 1, translation: true), Line("どれほど", 2)],
            LyricKind.WordByWord);

        var lines = LyricPosterComposer.Compose(document);

        Assert.Collection(
            lines,
            line => Assert.Equal(("夢ならば", false), (line.Text, line.IsTranslation)),
            line => Assert.Equal(("如果只是一场梦", true), (line.Text, line.IsTranslation)),
            line => Assert.Equal(("どれほど", false), (line.Text, line.IsTranslation)));
    }

    [Fact]
    public void BlankLinesAreDropped()
    {
        var document = new LyricDocument(
            [Line("第一句", 1), Line("", 2), Line("   ", 3), Line("第二句", 4)],
            LyricKind.LineByLine);

        var lines = LyricPosterComposer.Compose(document);

        Assert.Equal(["第一句", "第二句"], lines.Select(line => line.Text));
    }

    [Fact]
    public void TranslationOnlyDocumentKeepsEveryLine()
    {
        var document = new LyricDocument(
            [Line("译文一", 0, translation: true), Line("译文二", 1, translation: true)],
            LyricKind.WordByWord);

        var lines = LyricPosterComposer.Compose(document);

        Assert.Equal(["译文一", "译文二"], lines.Select(line => line.Text));
        Assert.All(lines, line => Assert.True(line.IsTranslation));
    }

    [Fact]
    public void IndexAtFindsCurrentLine()
    {
        var lines = LyricPosterComposer.Compose(new LyricDocument(
            [Line("第一句", 1), Line("第二句", 5), Line("第三句", 9)],
            LyricKind.LineByLine));

        Assert.Equal(-1, LyricPosterComposer.IndexAt(lines, TimeSpan.FromSeconds(0.5)));
        Assert.Equal(0, LyricPosterComposer.IndexAt(lines, TimeSpan.FromSeconds(1)));
        Assert.Equal(1, LyricPosterComposer.IndexAt(lines, TimeSpan.FromSeconds(6)));
        Assert.Equal(2, LyricPosterComposer.IndexAt(lines, TimeSpan.FromSeconds(60)));
    }
}
