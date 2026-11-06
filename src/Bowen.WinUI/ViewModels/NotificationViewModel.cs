using Bowen.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bowen.WinUI.ViewModels;

/// <summary>
/// 通知条上的一条：文案 + 等级 + 该挂多久。
/// </summary>
public sealed record NotificationEntry(string Message, NoticeSeverity Severity, TimeSpan Duration)
{
    /// <summary>同一句话、同一个等级 —— 用来合并连续重复，不让同一件事占两个坑。</summary>
    public bool SameAs(NotificationEntry other) =>
        Severity == other.Severity && string.Equals(Message, other.Message, StringComparison.Ordinal);
}

/// <summary>
/// 全应用唯一的短提示出口。<b>单例</b>。
/// </summary>
/// <remarks>
/// <para>
/// 约 30 个调用点（喜欢、收藏、入队、入歌单、复制链接、关注、播放被拒、音质降级…）都汇聚到这里，
/// 由外壳里那一个 <c>InfoBar</c> 呈现。在此之前它们走的是播放条上的同一个 <c>Notice</c> 字段，
/// 但那个字段混了两种语义（行内动作 3 秒消失、播放引擎写的挂到下一首开播），
/// 清场时得靠「序号 + 文案比对」去猜 —— 这里是把它拆干净的版本。
/// </para>
/// <para>
/// <b>同一时刻只显示一条，其余排队</b>：连续来多条时互相顶掉会让用户漏看，
/// 所以按到达顺序逐条播完。排队的代价是消息会滞后于动作发生的时间，
/// 所以队列有上限，满了丢**最旧的一条待播项** —— 最新的那条才刚发生，更该被看到。
/// </para>
/// <para>
/// <b>不碰 WinUI 类型</b>：等级是自家的 <see cref="NoticeSeverity"/>，
/// 到 <c>InfoBarSeverity</c> 的映射在 <c>Formats</c> 里做。
/// </para>
/// <para>
/// <b>全程在 UI 线程上</b>：调用方都是界面事件，计时用 <c>ConfigureAwait(true)</c> 回到原上下文，
/// 所以队列和计时器都不加锁。若将来有后台线程要发通知，得先切回 UI 线程再调 <see cref="Show"/>。
/// </para>
/// </remarks>
public sealed partial class NotificationViewModel : ObservableObject
{
    /// <summary>
    /// 不自动收起的时长哨兵。配合 <see cref="Show"/> 的 <c>duration</c> 参数使用。
    /// </summary>
    /// <remarks>
    /// <see cref="Timeout.InfiniteTimeSpan"/> 正好是 <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
    /// 认可的「无限等待」，所以不需要给计时逻辑加分支。
    /// </remarks>
    public static readonly TimeSpan Sticky = Timeout.InfiniteTimeSpan;

    /// <summary>待播队列的上限。见类型注释里「满了丢最旧」的理由。</summary>
    private const int MaxPending = 5;

    private static readonly TimeSpan InfoDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(5);

    private readonly List<NotificationEntry> _pending = [];
    private CancellationTokenSource? _dismissTimer;

    /// <summary>正在计时的那条；没有时是 <c>null</c>，通知条随之收起。队列与计时只看它。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial NotificationEntry? Current { get; set; }

    /// <summary>
    /// 通知条上留着的那一条。
    /// </summary>
    /// <remarks>
    /// <b><see cref="Current"/> 清空后不清</b>：收起是淡出动画，期间文案、等级圆点还得在，
    /// 否则会变成「字先没了、空条再淡出」。下一次通知到达时整体替换。
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Message))]
    [NotifyPropertyChangedFor(nameof(Severity))]
    [NotifyPropertyChangedFor(nameof(IsInformational))]
    [NotifyPropertyChangedFor(nameof(IsSuccess))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    public partial NotificationEntry? Displayed { get; set; }

    /// <summary>通知条的文案。空串时不显示。</summary>
    public string Message => Displayed?.Message ?? "";

    /// <summary>通知条的等级。空着时按中性处理，避免绑定取到 null。</summary>
    public NoticeSeverity Severity => Displayed?.Severity ?? NoticeSeverity.Informational;

    /// <summary>
    /// 等级的三个开关，给通知条上那颗圆点选配色用。
    /// </summary>
    /// <remarks>
    /// <b>三个布尔而不是一个枚举转画刷</b>：画刷要走 <c>{ThemeResource}</c> 才能在切换主题时重新求值，
    /// 而函数绑定与转换器都只在源属性变化时跑一次 —— 那种写法切主题后会留一个过期颜色。
    /// 拆成三颗点、各自绑死一个主题画刷，就没有这个问题。
    /// </remarks>
    public bool IsInformational => Severity == NoticeSeverity.Informational;

    /// <inheritdoc cref="IsInformational"/>
    public bool IsSuccess => Severity == NoticeSeverity.Success;

    /// <inheritdoc cref="IsInformational"/>
    public bool IsError => Severity == NoticeSeverity.Error;

    public bool IsOpen => Current is not null;

    /// <summary>
    /// 发一条通知。
    /// </summary>
    /// <param name="duration">单条挂多久。不传则按等级取默认值（错误挂久一些）。</param>
    public void Show(string message, NoticeSeverity severity = NoticeSeverity.Informational, TimeSpan? duration = null)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        var entry = new NotificationEntry(message, severity, duration ?? DefaultDuration(severity));

        // 正在显示的就是同一句话：只把计时重新起算，不再排一条。
        if (Current is { } showing && showing.SameAs(entry))
        {
            Display(showing);
            return;
        }

        // 队尾已经是同一句话：并进去，不重复排。
        if (_pending.Count > 0 && _pending[^1].SameAs(entry))
        {
            return;
        }

        while (_pending.Count >= MaxPending)
        {
            _pending.RemoveAt(0);
        }

        _pending.Add(entry);

        // 当前空着就直接上，否则等前一条到期时由 Advance 取。
        if (Current is null)
        {
            Advance();
        }
    }

    /// <summary>
    /// 显示一条不会自动消失的提示，直到 <see cref="Dismiss(string)"/> 按同一句话撤掉。
    /// </summary>
    public void ShowSticky(string message, NoticeSeverity severity = NoticeSeverity.Informational)
        => Show(message, severity, Sticky);

    /// <summary>
    /// 收起当前这条，立刻放下一条。
    /// </summary>
    /// <remarks>
    /// <b>不做成 RelayCommand</b>：关掉按钮走的是 <c>InfoBar.Closing</c> 事件
    /// （见 <c>MainWindow.OnNotificationClosing</c>），没有 XAML 绑命令 ——
    /// 挂一个没人绑的 <c>DismissCommand</c> 只是多一份要维护的生成代码。
    /// </remarks>
    public void Dismiss()
    {
        StopTimer();
        Current = null;
        Advance();
    }

    /// <summary>
    /// 撤掉指定文案的那条提示（正在显示的和还在排队的都撤）。
    /// </summary>
    /// <remarks>
    /// 给「条件恢复后自己收掉」的提示用，例如网络恢复时撤掉重连提示。
    /// 按文案比对而不是发一个句柄：这类提示全程只有一句固定的文案，句柄反而多一份状态要维护。
    /// </remarks>
    public void Dismiss(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        _pending.RemoveAll(entry => string.Equals(entry.Message, message, StringComparison.Ordinal));

        if (Current is { } current && string.Equals(current.Message, message, StringComparison.Ordinal))
        {
            Dismiss();
        }
    }

    private void Advance()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var next = _pending[0];
        _pending.RemoveAt(0);

        Display(next);
    }

    private void Display(NotificationEntry entry)
    {
        StopTimer();

        var timer = new CancellationTokenSource();
        _dismissTimer = timer;
        Current = entry;

        // 界面那份跟着换；Current 清空时它不清，留给收起动画。
        Displayed = entry;

        _ = HideAfterAsync(entry, timer.Token);
    }

    private async Task HideAfterAsync(NotificationEntry entry, CancellationToken token)
    {
        try
        {
            await Task.Delay(entry.Duration, token).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            // 被顶替或用户手动收起 —— 清场交给叫停的那一方，这里直接退出。
            return;
        }

        // 只有还停在原处才轮到它清场，否则可能把后来那条误清掉。
        if (ReferenceEquals(Current, entry))
        {
            Current = null;
            Advance();
        }
    }

    private void StopTimer()
    {
        var timer = _dismissTimer;
        _dismissTimer = null;

        if (timer is null)
        {
            return;
        }

        timer.Cancel();
        timer.Dispose();
    }

    private static TimeSpan DefaultDuration(NoticeSeverity severity) =>
        severity == NoticeSeverity.Error ? ErrorDuration : InfoDuration;
}
