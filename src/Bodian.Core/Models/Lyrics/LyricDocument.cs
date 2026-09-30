namespace Bodian.Core.Models.Lyrics;

/// <summary>歌词版式。</summary>
public enum LyricKind
{
    /// <summary>没有歌词。请求成功但服务端给的是空串时就是这个。</summary>
    None = 0,

    /// <summary>逐行。只有行时间轴，行内无逐字。</summary>
    LineByLine = 1,

    /// <summary>逐字。行内有音节级时间轴。</summary>
    WordByWord = 2,
}

/// <summary>
/// 一首歌的歌词。渲染层唯一需要认识的类型。
/// </summary>
/// <remarks>
/// <para>
/// <b>这是本项目自建的统一模型</b>，不依赖任何第三方歌词库：源生成的那几个库都不支持
/// AWLRC（行内 <c>&lt;a,b&gt;</c>），解析本来就得自己写，只借它们的模型容器得不偿失
/// （见 <c>docs/lyrics-ui.md</c> 第 1 节与计划里的决策①）。
/// </para>
/// <para>
/// <b>空文档不是异常</b>：服务端在「这首歌没有逐字轨」时返回空串，业务码依然是 200。
/// 用 <see cref="Empty"/> 表达，调用方不必区分「没歌词」和「取词失败」——
/// 对界面来说两者都是「这首歌没词」。
/// </para>
/// </remarks>
/// <param name="Lines">按时间升序的行。空文档时是空数组。</param>
/// <param name="Kind">版式。</param>
public sealed record LyricDocument(IReadOnlyList<LyricLine> Lines, LyricKind Kind)
{
    /// <summary>没有歌词。</summary>
    public static LyricDocument Empty { get; } = new([], LyricKind.None);

    /// <summary>没有歌词。</summary>
    public bool IsEmpty => Lines.Count == 0;

    /// <summary>
    /// 二分查找 <paramref name="position"/> 落在哪一行。
    /// </summary>
    /// <returns>行下标；位置在第一行之前时返回 <c>-1</c>。</returns>
    /// <remarks>
    /// 唱片头空白段时返回 <c>-1</c>，界面据此不高亮任何一行。
    /// 曲末之后仍返回最后一行——那时保持末行高亮比清空更自然。
    /// </remarks>
    public int IndexOfLineAt(TimeSpan position)
    {
        var low = 0;
        var high = Lines.Count - 1;
        var found = -1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);

            if (Lines[mid].Start <= position)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }
}
