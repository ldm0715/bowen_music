namespace Bodian.Core.Navigation;

/// <summary>
/// 页面栈。**纯逻辑，零 UI 依赖**，泛型到页面类型由宿主决定。
/// </summary>
/// <remarks>
/// <para>
/// 放在 <c>Bodian.Core</c> 而不是 WinUI 工程里，是为了能单测。「换根」「实例复用」
/// 「栈底即根」这三条是本项目侧栏语义的全部内容，错了表现为「侧栏高亮错一项」这种
/// 不痛不痒、靠肉眼很难发现的现象 —— 必须有测试守着。同样的理由让
/// <c>SessionIdentity</c> 被提进了 Core。
/// </para>
/// <para>
/// <b>身份（identity）而不是类型决定「是不是同一个页面」。</b>「我喜欢的」和每个自建歌单详情
/// 若按类型判等，点第二个歌单会被当成「已经是这个页面」而不切换。所以判等统一走
/// <c>identity</c> 函数：无参根页给 <c>typeof(TPage)</c>，带参根页给自己的 id。
/// </para>
/// </remarks>
/// <param name="identity">
/// 从页面取出它的身份。**同一身份视为同一个页面**，重复导航时复用已有实例、不新建。
/// </param>
public sealed class NavigationStack<T>(Func<T, object> identity)
    where T : class
{
    /// <summary>当前页之前的页面，按先后顺序。<b>不含当前页。</b></summary>
    private readonly List<T> _history = [];

    private readonly Func<T, object> _identity =
        identity ?? throw new ArgumentNullException(nameof(identity));

    /// <summary>当前页。</summary>
    public T? Current { get; private set; }

    /// <summary>
    /// 根页：侧栏该高亮的那一项。
    /// </summary>
    /// <remarks>
    /// <b>栈底就是根</b>。从「我喜欢的」点进一个歌单详情，详情压在栈上、栈底仍是「我喜欢的」，
    /// 侧栏就该继续高亮「我喜欢的」——这正是「栈底即根」这条规则的用途。
    /// </remarks>
    public T? Root => _history.Count > 0 ? _history[0] : Current;

    public bool CanGoBack => _history.Count > 0;

    /// <summary>当前页之前的页面，最靠近当前页的在最后。</summary>
    public IReadOnlyList<T> History => _history;

    /// <summary>
    /// 压栈切页。当前页已经是同一个身份时什么都不做。
    /// </summary>
    /// <returns>调用之后真正应该显示的页面（可能是原当前页，也可能是新页）。</returns>
    public T Push(T page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (Current is { } current && Same(current, page))
        {
            return current;
        }

        if (Current is { } previous)
        {
            _history.Add(previous);
        }

        Current = page;
        return page;
    }

    /// <summary>
    /// 换根（侧栏点击的语义）。<b>目标是新的根，历史全部丢弃。</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>不能沿用压栈语义</b>：在「我喜欢的 → 歌单详情」里点侧栏的「创建的歌单」，
    /// 压栈会得到 <c>[我喜欢的, 歌单详情, 创建的歌单]</c>，栈底仍是「我喜欢的」，
    /// 侧栏高亮就错了。
    /// </para>
    /// <para>
    /// <b>也不能只「截断到目标所在位置」</b>：栈底即根的规则下，那会让目标<b>下面</b>的页面
    /// 继续当根。反例是「创建的歌单 → 某个歌单详情」之后点侧栏里那个歌单 ——
    /// 截断会把「创建的歌单」留在栈底，侧栏高亮的仍是列表页而不是刚点的歌单。
    /// 所以历史一律清空，目标成为唯一的根。
    /// </para>
    /// <para>
    /// <b>但实例要复用</b>：同一个身份已经在栈里就还它，别新建 —— 搜索结果、滚动位置
    /// 都还在（那正是当初放弃 <c>Frame</c> 的理由）。
    /// </para>
    /// <para>
    /// 代价是换根之后 <see cref="CanGoBack"/> 必为假，即侧栏点击等于「重置到某一段」，
    /// 与标签页的行为一致。
    /// </para>
    /// </remarks>
    /// <returns>调用之后真正应该显示的页面（命中时是已有实例，否则是传入的新页）。</returns>
    public T NavigateRoot(T page)
    {
        ArgumentNullException.ThrowIfNull(page);

        // 连同当前页一起找：当前页也可能就是目标（重复点同一个侧栏项）。
        var full = new List<T>(_history);

        if (Current is { } current)
        {
            full.Add(current);
        }

        var existing = full.Find(candidate => Same(candidate, page)) ?? page;

        _history.Clear();
        Current = existing;
        return existing;
    }

    /// <summary>清栈换页。登录成功、被动登出这类「不该退回去」的跳转用它。</summary>
    public T Reset(T page)
    {
        ArgumentNullException.ThrowIfNull(page);

        _history.Clear();
        Current = page;
        return page;
    }

    /// <summary>回退一页。栈空时什么都不做，返回 <c>null</c>。</summary>
    public T? GoBack()
    {
        if (_history.Count == 0)
        {
            return null;
        }

        var previous = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        Current = previous;
        return previous;
    }

    private bool Same(T left, T right) => _identity(left).Equals(_identity(right));
}
