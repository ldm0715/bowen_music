namespace Bodian.Core.Models.Lyrics;

/// <summary>
/// 歌词的一行（含它的音节与时间轴）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Duration"/> 不是服务端给的，是「下一行行首 − 本行行首」推出来的；
/// 最后一行用曲目时长或兜底值收尾。所以它是<b>行的时间窗口</b>，
/// 不等于最后一个音节的结束时间。
/// </para>
/// <para>
/// <see cref="Text"/> 与 <see cref="Syllables"/> 是同源的：把所有音节的文本拼起来就是
/// <see cref="Text"/>。渲染层用前者排版、用后者做逐字高亮，两边必须对得上。
/// </para>
/// <para>
/// 「这一行有没有逐字时间轴」不在这里判断 —— 版式是整份文档的属性，看
/// <see cref="LyricDocument.Kind"/>。按音节数猜会在一行只有一个音节的逐字轨上判错。
/// </para>
/// </remarks>
/// <param name="Start">行首绝对时间。</param>
/// <param name="Duration">行的时间窗口长度。</param>
/// <param name="Text">去掉全部 <c>&lt;a,b&gt;</c> 标记后的整行文本。</param>
/// <param name="Syllables">音节明细。逐行版只有一个覆盖整行的音节。</param>
public sealed record LyricLine(
    TimeSpan Start,
    TimeSpan Duration,
    string Text,
    IReadOnlyList<LyricSyllable> Syllables)
{
    /// <summary>行的时间窗口结束。</summary>
    public TimeSpan End => Start + Duration;
}
