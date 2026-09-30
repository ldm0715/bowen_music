namespace Bodian.Core.Models.Lyrics;

/// <summary>
/// 歌词里的一个音节：一小段文本加上它的时间轴。
/// </summary>
/// <remarks>
/// <para>
/// <b>逐行版也有音节</b>：整行就是一个音节（<see cref="Start"/> 与行首重合、
/// <see cref="Duration"/> 与行等长）。这样渲染层不必为两种版式各写一套取时间的逻辑。
/// </para>
/// <para>
/// <see cref="Start"/> 是<b>绝对时间</b>，不是酷我原始报文里的行内相对偏移——
/// 相对偏移在解析阶段就折算了，见 <c>BodianLyricParser</c>。
/// </para>
/// </remarks>
/// <param name="Text">这一小段的文本。可能是空白（长音之间的间隔就落在这种音节上）。</param>
/// <param name="Start">绝对起始时间。</param>
/// <param name="Duration">持续时长。</param>
public sealed record LyricSyllable(string Text, TimeSpan Start, TimeSpan Duration)
{
    /// <summary>结束时间。</summary>
    public TimeSpan End => Start + Duration;
}
