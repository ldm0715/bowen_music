using Bodian.Core.Models;

namespace Bodian.WinUI.Playback;

/// <summary>
/// 播放队列。
/// </summary>
/// <remarks>
/// <para>
/// 队列来源是<b>当前页面点播时的那一整页结果</b>：点第 N 行 → 整页入队、从第 N 首开始。
/// 「能连着播」指的是页内连续播完，不是自动翻页拉更多。
/// </para>
/// <para>
/// <b>内部是「排列 + 游标」，不是单个下标。</b> <c>_items</c> 的顺序就是队列面板里给用户看到的顺序，
/// <c>_order</c> 是 <c>_items</c> 下标的一个排列，<c>_cursor</c> 是它在 <c>_order</c> 里的位置。
/// 顺序播放与列表循环时 <c>_order</c> 是恒等排列；列表随机时是洗过牌的排列 ——
/// 这样「随机」既不打乱面板上给用户看的顺序，一轮里也不会重样。
/// </para>
/// <para>
/// <b>增删曲目必须同时重映射 <c>_order</c> 里的下标</b>，这是本类最容易错的地方：
/// 往 <c>_items</c> 的第 i 处插入，会让所有 &gt;= i 的下标失效；删除则会让 &gt; i 的整体前移。
/// </para>
/// </remarks>
public sealed class PlayQueue
{
    private readonly List<Track> _items = [];
    private readonly List<int> _order = [];
    private readonly Random _random;
    private int _cursor = -1;
    private PlayMode _mode = PlayMode.Sequential;

    /// <param name="random">
    /// 洗牌用。<b>只给测试传</b>：定种子的实例能得到确定的排列，方便断言。
    /// </param>
    public PlayQueue(Random? random = null) => _random = random ?? Random.Shared;

    /// <summary>队列变化（换页、增删、切歌、改模式）时触发。</summary>
    public event EventHandler? Changed;

    /// <summary>队列本体。<b>顺序即面板上显示的顺序。</b></summary>
    public IReadOnlyList<Track> Items => _items;

    public int Count => _items.Count;

    /// <summary>
    /// 当前曲目在 <see cref="Items"/> 里的下标，空队列为 <c>-1</c>。
    /// </summary>
    /// <remarks>
    /// 队列面板用它决定「正在播放」的高亮落在哪一行 —— 所以这里给的是**显示下标**，不是内部游标。
    /// </remarks>
    public int CurrentIndex => _cursor >= 0 && _cursor < _order.Count ? _order[_cursor] : -1;

    public Track? Current => CurrentIndex is var index && index >= 0 ? _items[index] : null;

    /// <summary>
    /// 还能不能往后走。
    /// </summary>
    /// <remarks>
    /// <b>模式判断收在这里，不要在调用方各写一遍</b>：<c>SmtcManager</c> 与播放条都直接读这两个属性，
    /// 放在这里，系统媒体控件的上一首/下一首按钮就自动跟着模式变灰或变亮。
    /// </remarks>
    public bool HasNext => _cursor >= 0 && _order.Count > 0
        && (_mode != PlayMode.Sequential || _cursor < _order.Count - 1);

    /// <summary>还能不能往回走。列表循环里第一首的上一首是最后一首，所以恒为真。</summary>
    public bool HasPrevious => _order.Count > 0
        && (_mode == PlayMode.ListLoop ? _cursor >= 0 : _cursor > 0);

    /// <summary>播放模式。改它会重建排列，但<b>不会换掉当前曲目</b>。</summary>
    public PlayMode Mode
    {
        get => _mode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "未知的播放模式");
            }

            if (_mode == value)
            {
                return;
            }

            _mode = value;

            // CurrentIndex 读的是旧排列，必须在 ApplyOrderForStart 重建之前取出来。
            if (_items.Count > 0)
            {
                ApplyOrderForStart(CurrentIndex);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>用一整页结果替换队列，并从指定位置开始。</summary>
    public void Replace(IReadOnlyList<Track> items, int startIndex)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        _items.AddRange(items);

        if (_items.Count == 0)
        {
            _order.Clear();
            _cursor = -1;
        }
        else
        {
            ApplyOrderForStart(Math.Clamp(startIndex, 0, _items.Count - 1));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 加到队尾。
    /// </summary>
    /// <remarks>
    /// 队列原本是空的时候把这一首放上去并落游标 —— 调用方据此决定要不要开播，
    /// 见 <c>PlaybackCoordinator.AddToQueueAsync</c>。
    /// </remarks>
    public void Append(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (_items.Count == 0)
        {
            FillEmptyQueue(track);
            return;
        }

        _items.Add(track);
        _order.Add(_items.Count - 1);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>插到当前曲目之后。队列为空或没有当前曲目时等同于 <see cref="Append"/>。</summary>
    public void InsertNext(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (_cursor < 0)
        {
            FillEmptyQueue(track);
            return;
        }

        var itemIndex = CurrentIndex + 1;
        _items.Insert(itemIndex, track);

        for (var i = 0; i < _order.Count; i++)
        {
            if (_order[i] >= itemIndex)
            {
                _order[i]++;
            }
        }

        _order.Insert(_cursor + 1, itemIndex);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>跳到队列里第 <paramref name="itemIndex"/> 首。</summary>
    /// <returns>下标越界返回 <c>false</c>。</returns>
    public bool MoveToItem(int itemIndex)
    {
        var position = _order.IndexOf(itemIndex);

        if (position < 0)
        {
            return false;
        }

        _cursor = position;
        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// 删掉队列里第 <paramref name="itemIndex"/> 首。
    /// </summary>
    /// <remarks>
    /// <b>删掉的正好是当前曲目时，游标留在原位</b>，也就是「当前曲目」变成原本的下一首 ——
    /// 本类不负责决定要不要接着播，那是协调器的事。界面把正在播放那一行的删除按钮置灰，
    /// 走不到这个分支。
    /// </remarks>
    /// <returns>删掉了返回 <c>true</c>；下标越界返回 <c>false</c>。</returns>
    public bool RemoveItem(int itemIndex)
    {
        var position = _order.IndexOf(itemIndex);

        if (position < 0)
        {
            return false;
        }

        var removed = _order[position];

        _items.RemoveAt(removed);
        _order.RemoveAt(position);

        for (var i = 0; i < _order.Count; i++)
        {
            if (_order[i] > removed)
            {
                _order[i]--;
            }
        }

        if (_order.Count == 0)
        {
            _cursor = -1;
        }
        else if (position < _cursor)
        {
            _cursor--;
        }
        else if (_cursor >= _order.Count)
        {
            // 删掉的是末尾那一首，游标得退回最后一个位置。
            _cursor = _order.Count - 1;
        }

        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>移到下一首。到不到头由 <see cref="Mode"/> 决定。</summary>
    public bool MoveNext()
    {
        if (!HasNext)
        {
            return false;
        }

        switch (_mode)
        {
            case PlayMode.ListLoop:
                _cursor = (_cursor + 1) % _order.Count;
                break;
            case PlayMode.Shuffle:
                if (_cursor < _order.Count - 1)
                {
                    _cursor++;
                }
                else
                {
                    // 一轮走完：重新洗牌接着放，并避开刚放完的这一首。
                    Reshuffle(keepFirst: null, avoidFirst: CurrentIndex);
                    _cursor = 0;
                }
                break;
            default:
                _cursor++;
                break;
        }

        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>移到上一首。列表循环会从第一首绕到最后一首，随机模式往回走到头就停。</summary>
    public bool MovePrevious()
    {
        if (!HasPrevious)
        {
            return false;
        }

        _cursor = _mode == PlayMode.ListLoop
            ? (_cursor - 1 + _order.Count) % _order.Count
            : _cursor - 1;

        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _order.Clear();
        _cursor = -1;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>按当前模式重建排列，并让 <paramref name="startItemIndex"/> 成为当前曲目。</summary>
    private void ApplyOrderForStart(int startItemIndex)
    {
        if (_mode == PlayMode.Shuffle)
        {
            Reshuffle(keepFirst: startItemIndex, avoidFirst: -1);
            _cursor = 0;
            return;
        }

        ResetToIdentityOrder();
        _cursor = startItemIndex;
    }

    private void ResetToIdentityOrder()
    {
        _order.Clear();

        for (var i = 0; i < _items.Count; i++)
        {
            _order.Add(i);
        }
    }

    /// <summary>重新洗牌并重建 <c>_order</c>。</summary>
    /// <param name="keepFirst">洗完后要排在第一位的那一首；<c>null</c> 表示随便排。</param>
    /// <param name="avoidFirst">不要排在第一位的那一首，避免一轮结束时立刻重播；<c>-1</c> 表示不避讳。</param>
    private void Reshuffle(int? keepFirst, int avoidFirst)
    {
        ResetToIdentityOrder();

        for (var i = _order.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }

        if (keepFirst is { } first)
        {
            SwapToFront(_order.IndexOf(first));
        }
        else if (avoidFirst >= 0 && _order.Count > 1 && _order[0] == avoidFirst)
        {
            SwapToFront(_random.Next(1, _order.Count));
        }
    }

    private void SwapToFront(int position)
    {
        if (position > 0)
        {
            (_order[0], _order[position]) = (_order[position], _order[0]);
        }
    }

    /// <summary>把一首歌放进空队列。调用方负责保证队列确实是空的。</summary>
    private void FillEmptyQueue(Track track)
    {
        _items.Add(track);
        _order.Clear();
        _order.Add(0);
        _cursor = 0;

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
