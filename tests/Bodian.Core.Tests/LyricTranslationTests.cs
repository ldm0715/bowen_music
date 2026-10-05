using Bodian.Core.Lyrics;
using Bodian.Core.Models.Lyrics;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 译文行的识别与剔除。
/// </summary>
/// <remarks>
/// <para>
/// <b>样本按真实响应的形状手写</b>，不是凭空造的：外文歌那份内容里，原文行与中文译文行成对出现，
/// 两行<b>行首时间戳完全相同</b>，译文行的逐字标签全是 <c>&lt;0,0&gt;</c>（解出来时长为 0），
/// 原文行有真实时长。首行的 <c>[kuwo:027]</c> 是八进制 23，即 startFactor 2 / durationFactor 3。
/// </para>
/// <para>
/// <b>为什么不用整首真实歌词当 fixture</b>：这里要守的是「成对关系怎么判」，
/// 用最短能表达该关系的片段就够，不必把整首歌的文本再提交一份进仓库。
/// </para>
/// </remarks>
public sealed class LyricTranslationTests
{
    /// <summary>空行 + 原文，随后两组成对的「译文 + 原文」。</summary>
    private const string PairedSample = """
        [kuwo:027]
        [ver:v1.0]
        [00:01.547]
        [00:01.547]<99,-99>夢<264,-132>な<297,99>ら<464,68>ば
        [00:02.880]<0,0>如<0,0>果<0,0>只<0,0>是<0,0>一<0,0>场<0,0>梦
        [00:02.880]<1128,-1128>ど<1688,-280>れ<2480,-976>ほ<3176,632>ど
        [00:06.882]<0,0>那<0,0>该<0,0>有<0,0>多<0,0>好
        [00:06.882]<1056,-1056>未<1688,-280>だ<1888,832>に<4232,-808>あ
        """;

    private static bool[] Marked(LyricDocument document)
    {
        var result = new bool[document.Lines.Count];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = document.Lines[i].IsTranslation;
        }

        return result;
    }

    [Fact]
    public void PairedSample_MarksOnlyTheUntimedLines()
    {
        var document = BodianLyricParser.Parse(PairedSample);

        Assert.Equal(6, document.Lines.Count);
        Assert.Equal([true, false, true, false, true, false], Marked(document));

        // 被标中的就是那三行全 <0,0> 的；空行与三行原文都不该被标。
        Assert.Equal("", document.Lines[0].Text);
        Assert.Equal("夢ならば", document.Lines[1].Text);
        Assert.Equal("如果只是一场梦", document.Lines[2].Text);
        Assert.Equal("どれほど", document.Lines[3].Text);
        Assert.Equal("那该有多好", document.Lines[4].Text);
        Assert.Equal("未だにあ", document.Lines[5].Text);
    }

    /// <summary>判据只看逐字时间，不看位置。</summary>
    [Fact]
    public void TranslationBeforeOriginal_IsStillMarked()
    {
        var document = BodianLyricParser.Parse(
            "[kuwo:027]\n[00:01.00]<0,0>译<0,0>文\n[00:01.00]<99,-99>原<264,-132>文");

        Assert.Equal([true, false], Marked(document));
    }

    /// <summary>中文歌没有成对行，一行都不该被标。</summary>
    [Fact]
    public void WordByWordFixture_MarksNothing()
    {
        var document = BodianLyricParser.Parse(Fixtures.Read("lyric-228908-lrcx1.lrc"));

        Assert.All(document.Lines, line => Assert.False(line.IsTranslation));
    }

    /// <summary>
    /// 逐行版（<c>lrcx=0</c>）认不出译文 —— <b>已知限制</b>，不是遗漏。
    /// </summary>
    /// <remarks>
    /// 逐行版没有任何 <c>&lt;a,b&gt;</c> 标签，组内两行都「没有真实逐字时间」，
    /// 判据分不出哪条是译文。这时一律不标：宁可让开关点了没反应，也不能误删真歌词。
    /// </remarks>
    [Fact]
    public void LineByLinePaired_IsNotMarked()
    {
        var document = BodianLyricParser.Parse("[00:01.00]原文\n[00:01.00]译文");

        Assert.Equal(2, document.Lines.Count);
        Assert.All(document.Lines, line => Assert.False(line.IsTranslation));
    }

    /// <summary>一行挂两个相同时间戳（副歌复用的老写法）不算成对，不该误标。</summary>
    [Fact]
    public void DuplicateTimestampOnOneLine_IsNotMarked()
    {
        var document = BodianLyricParser.Parse("[kuwo:027]\n[00:01.00][00:01.00]<99,-99>同<264,-132>一<297,99>句");

        Assert.Equal(2, document.Lines.Count);
        Assert.All(document.Lines, line => Assert.False(line.IsTranslation));
    }

    /// <summary>三行同一时间戳时，没有逐字时间的两行都算译文。</summary>
    [Fact]
    public void GroupOfThree_MarksEveryUntimedLine()
    {
        var document = BodianLyricParser.Parse(
            "[kuwo:027]\n[00:01.00]<0,0>译<0,0>一\n[00:01.00]<99,-99>原<264,-132>文\n[00:01.00]<0,0>译<0,0>二");

        Assert.Equal([true, false, true], Marked(document));
    }

    // ── 有没有译文（界面拿它决定开关能不能点）────────────────────────────────

    [Fact]
    public void HasTranslation_TrueForPairedSample()
    {
        Assert.True(BodianLyricParser.Parse(PairedSample).HasTranslation);
    }

    [Fact]
    public void HasTranslation_FalseForChineseSongs()
    {
        Assert.False(BodianLyricParser.Parse(Fixtures.Read("lyric-228908-lrcx1.lrc")).HasTranslation);
    }

    [Fact]
    public void HasTranslation_FalseWhenThereIsNoLyricAtAll()
    {
        Assert.False(LyricDocument.Empty.HasTranslation);
    }

    /// <summary>
    /// 剔除之后就问不出来了 —— 界面必须在<b>完整</b>文档上问这个问题。
    /// </summary>
    /// <remarks>
    /// 拿过滤后的 <c>Document</c> 去问 <c>HasTranslation</c> 永远是 <c>false</c>，
    /// 那颗开关就会在译文开着的时候自己禁掉，点都点不回去。
    /// <c>LyricsViewModel.SyncDocument</c> 先问 <c>_fullDocument</c> 再投影，守的就是这条。
    /// </remarks>
    [Fact]
    public void HasTranslation_FalseAfterStripping()
    {
        Assert.False(BodianLyricParser.Parse(PairedSample).WithoutTranslations().HasTranslation);
    }

    // ── 剔除 ────────────────────────────────────────────────────────────────

    [Fact]
    public void WithoutTranslations_KeepsTheOriginalLines()
    {
        var document = BodianLyricParser.Parse(PairedSample);

        var stripped = document.WithoutTranslations();

        Assert.Equal(3, stripped.Lines.Count);
        Assert.Equal(LyricKind.WordByWord, stripped.Kind);
        Assert.All(stripped.Lines, line => Assert.False(line.IsTranslation));
        Assert.Equal("夢ならば", stripped.Lines[0].Text);
    }

    /// <summary>
    /// 剔除不改变行的时间窗口。
    /// </summary>
    /// <remarks>
    /// 成对的两行行首相同，剔掉译文后「下一行行首 − 本行行首」算出来的还是同一个值。
    /// 这条要是破了，逐字扫光与滚动位置会跟着一起错。
    /// </remarks>
    [Fact]
    public void WithoutTranslations_KeepsStartsAndDurations()
    {
        var document = BodianLyricParser.Parse(PairedSample);

        var stripped = document.WithoutTranslations();

        for (var i = 0; i < stripped.Lines.Count; i++)
        {
            Assert.Equal(document.Lines[(i * 2) + 1].Start, stripped.Lines[i].Start);
            Assert.Equal(document.Lines[(i * 2) + 1].Duration, stripped.Lines[i].Duration);
        }
    }

    [Fact]
    public void WithoutTranslations_ReturnsSameInstanceWhenThereIsNothingToDrop()
    {
        var document = BodianLyricParser.Parse("[00:01.00]第一行\n[00:03.00]第二行");

        Assert.Same(document, document.WithoutTranslations());
    }

    [Fact]
    public void WithoutTranslations_KeepsLookupBehaviour()
    {
        var stripped = BodianLyricParser.Parse(PairedSample).WithoutTranslations();

        Assert.Equal(-1, stripped.IndexOfLineAt(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, stripped.IndexOfLineAt(TimeSpan.FromMilliseconds(1547)));
        Assert.Equal(1, stripped.IndexOfLineAt(TimeSpan.FromMilliseconds(2880)));
        Assert.Equal(2, stripped.IndexOfLineAt(TimeSpan.FromSeconds(60)));
    }
}
