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
    /// <summary>
    /// 这一行是译文（而非原文）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只对<b>外文歌</b>成立：波点那份歌词内容里，原文行与中文译文行成对出现，
    /// 两行的行首时间戳完全相同，译文行的逐字标签全是 <c>&lt;0,0&gt;</c>。
    /// 判据与边界见 <c>BodianLyricParser</c> 的标记 pass。
    /// </para>
    /// <para>
    /// <b>它是标记，不是排版属性。</b> 渲染层照旧只认 <see cref="Text"/> 与
    /// <see cref="Syllables"/>；要让译文不显示，是把整行从文档里剔掉
    /// （<see cref="LyricDocument.WithoutTranslations"/>），而不是让渲染层跳过它 ——
    /// 否则行高与滚动位置的计算就得再分一套。
    /// </para>
    /// </remarks>
    public bool IsTranslation { get; init; }

    /// <summary>行的时间窗口结束。</summary>
    public TimeSpan End => Start + Duration;
}
