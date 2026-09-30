using Bodian.Core.Models;

namespace Bodian.WinUI.Playback;

/// <summary>
/// 播放队列。
/// </summary>
/// <remarks>
/// P2 的队列来源就是<b>当前搜索页的结果列表</b>：点第 N 行 → 整页入队并从第 N 首开始。
/// 「能连着播」指的是页内连续播完，不是自动翻页拉更多 —— 后者要等 P7 做成能按需取页的惰性队列。
/// </remarks>
public sealed class PlayQueue
{
    private readonly List<Track> _items = [];
    private int _index = -1;

    /// <summary>队列变化（换页、清空）时触发。</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<Track> Items => _items;

    /// <summary>当前曲目下标，空队列为 <c>-1</c>。</summary>
    public int CurrentIndex => _index;

    public Track? Current => _index >= 0 && _index < _items.Count ? _items[_index] : null;

    public bool HasNext => _index >= 0 && _index < _items.Count - 1;

    public bool HasPrevious => _index > 0;

    /// <summary>用一整页结果替换队列，并从指定位置开始。</summary>
    public void Replace(IReadOnlyList<Track> items, int startIndex)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        _items.AddRange(items);
        _index = _items.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _items.Count - 1);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>移到下一首。没有下一首时返回 <c>false</c>，不做循环。</summary>
    public bool MoveNext()
    {
        if (!HasNext)
        {
            return false;
        }

        _index++;
        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    public bool MovePrevious()
    {
        if (!HasPrevious)
        {
            return false;
        }

        _index--;
        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _index = -1;

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
