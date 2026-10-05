using Bodian.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bodian.WinUI.ViewModels;

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
    /// <summary>待播队列的上限。见类型注释里「满了丢最旧」的理由。</summary>
    private const int MaxPending = 5;

    private static readonly TimeSpan InfoDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(5);

    private readonly List<NotificationEntry> _pending = [];
    private CancellationTokenSource? _dismissTimer;

    /// <summary>当前显示中的那条；没有时是 <c>null</c>，通知条随之收起。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Message))]
    [NotifyPropertyChangedFor(nameof(Severity))]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial NotificationEntry? Current { get; set; }

    /// <summary>通知条的文案。空串时不显示。</summary>
    public string Message => Current?.Message ?? "";

    /// <summary>通知条的等级。空着时按中性处理，避免绑定取到 null。</summary>
    public NoticeSeverity Severity => Current?.Severity ?? NoticeSeverity.Informational;

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
