using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Models.Account;
using Bodian.WinUI.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 标题栏右上角账号按钮的下拉框。
/// </summary>
/// <remarks>
/// <b>属性必须在账号变化时自己发通知。</b> 账号按钮是主窗口构造时就建好的，那一刻还没有会话
/// （登录页还没走完），而 <c>x:Bind</c> 默认是 OneTime —— 不发通知的话，
/// 登录完成后头像与昵称会永远停在空白处。
/// </remarks>
public sealed partial class AccountViewModel : ObservableObject
{
    private readonly IBodianLogin _login;
    private readonly IBodianApi _api;
    private readonly BodianSession _session;
    private readonly ILogger<AccountViewModel> _logger;

    /// <summary>
    /// 本 VM 属性通知的归属线程。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="MainWindow"/> 在 UI 线程上构造，所以这里取到的就是主窗口队列。
    /// 没有它，<see cref="OnAccountChanged"/> 从线程池过来时会去改 x:Bind 的目标对象并抛 0x8001010E。
    /// </remarks>
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();

    private AccountMetadata? _metadata;
    private AccountPlayData? _playData;
    private AccountVipInfo? _vip;
    private bool _statsLoading;
    private CancellationTokenSource? _statsCts;

    public AccountViewModel(
        IBodianLogin login,
        IBodianApi api,
        BodianSession session,
        ILogger<AccountViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(session);

        _login = login;
        _api = api;
        _session = session;
        _logger = logger ?? NullLogger<AccountViewModel>.Instance;

        _login.AccountChanged += OnAccountChanged;
    }

    /// <summary>账号显示名。没有昵称就退回 uid。</summary>
    public string AccountText => _login.Nickname ?? _login.Account?.Uid ?? "已登录";

    /// <summary>账号头像。没有（或老凭据文件里没存）时为 <c>null</c>，界面显示占位。</summary>
    public ImageSource? Avatar
    {
        get
        {
            var uri = _login.Account?.Avatar;

            // 标题栏和账号弹层复用同一张缩略图，避免每次绑定解码一张原尺寸头像。
            return CoverImageCache.Get(uri, 128);
        }
    }

    /// <summary>
    /// 会员档位。**实时值优先，拿不到才退回登录时存进凭据的快照。**
    /// </summary>
    /// <remarks>
    /// 快照只反映登录那一刻；会员在登录期间变化时，要等下一次打开下拉框重算才会跟。
    /// </remarks>
    private VipBadgeKind Badge =>
        _vip?.Badge ?? _login.Account?.VipBadge ?? VipBadgeKind.None;

    /// <summary>会员到期时间。与 <see cref="Badge"/> 同款：实时值优先，失效才退回快照。</summary>
    private DateTimeOffset? ExpiresAt =>
        _vip is { } live ? live.ExpiresAt : _login.Account?.VipExpiresAt;

    /// <summary>是否会员。**只用于展示**，播放权限一律以服务端 checkRight 为准。</summary>
    public bool IsVip => Badge != VipBadgeKind.None;

    /// <summary>会员档位名，与图标一起显示在胶囊里。</summary>
    public string VipKindLabel => Badge switch
    {
        VipBadgeKind.Big => "大会员",
        VipBadgeKind.Standard => "畅听会员",
        VipBadgeKind.Welfare => "福利会员",
        _ => "VIP",
    };

    /// <summary>
    /// 会员到期时间文案，显示在胶囊同一行的小字。
    /// </summary>
    /// <remarks>
    /// 数据来自 <c>payInfo</c> 的到期字段（取最晚的一个）。非会员或完全拿不到时是空串，
    /// 由 <see cref="VipExpiryVisibility"/> 收掉。
    /// </remarks>
    public string VipExpiryText => VipExpiryLabel.Format(ExpiresAt);

    /// <summary>只在「是会员且拿得到到期时间」时显示。</summary>
    public Visibility VipExpiryVisibility => Visible(IsVip && VipExpiryText.Length > 0);

    /// <summary>畅听会员图标可见性。</summary>
    public Visibility StandardVipVisibility => VisibleFor(VipBadgeKind.Standard);

    /// <summary>大会员图标可见性。</summary>
    public Visibility BigVipVisibility => VisibleFor(VipBadgeKind.Big);

    /// <summary>福利会员图标可见性。</summary>
    public Visibility WelfareVipVisibility => VisibleFor(VipBadgeKind.Welfare);

    /// <summary>
    /// 关注歌手数文案。取的是 <c>followArtistCount</c> 而<b>不是</b> <c>followCount</c> ——
    /// 界面要的是「关注了几个歌手」，不是「关注了几个用户」。
    /// </summary>
    /// <remarks>大数走与歌手页粉丝数同一套缩写；拿不到时显示「—」而不是 0。</remarks>
    public string FollowedArtistCountText =>
        _metadata?.FollowArtistCount is { } count ? CommentCountLabel.Format(count) : "—";

    /// <summary>听歌时长文案。格式化在 Core 的 <see cref="ListenTimeLabel"/> 里，那里可单测。</summary>
    public string ListenTimeText =>
        ListenTimeLabel.Format(_playData?.PlaySeconds) is { Length: > 0 } text ? text : "—";

    /// <summary>整块统计的可见性：两个数都没拿到时收掉，不留一行「—  —」。</summary>
    public Visibility StatsVisibility =>
        Visible(_metadata?.FollowArtistCount is not null || _playData?.PlaySeconds is not null);

    /// <summary>
    /// 拉一次账号统计。由下拉框的 <c>Opening</c> 调用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>每次打开都重拉，不做会话级缓存。</b>关注歌手数与听歌时长都会随时间变，
    /// 缓存住会让用户看到的是上次打开时的旧数 —— 而这个弹层本来就是按需打开的，
    /// 一次往返的代价换「点开即最新」是划算的。
    /// </para>
    /// <para>
    /// <b>不在 <see cref="OnAccountChanged"/> 里拉</b>：那是启动/登录瞬间，用户可能一整天都不点开
    /// 下拉框；而且启动路径上侧栏已经在发请求了。打开下拉框说明用户正在看它，这时候发才值得。
    /// </para>
    /// <para>
    /// <b>上一次还在飞就跳过</b>（<see cref="_statsLoading"/>）：连点几次不该叠出几串请求。
    /// 旧值在结果回来前继续显示，不做清空 —— 清空会让每次打开都闪一下「—」。
    /// </para>
    /// <para>
    /// <b>内部吞掉所有异常</b>：调用点是 fire-and-forget，抛出去会变成
    /// <c>TaskScheduler.UnobservedTaskException</c>，而 <c>App</c> 把它记成 Critical 日志 ——
    /// 一个统计拉不到不值得留一条 Critical。失败时保留上一次的旧值，不改成「—」。
    /// </para>
    /// </remarks>
    public async Task RefreshStatsAsync()
    {
        if (!_session.IsAuthenticated || _statsLoading)
        {
            return;
        }

        _statsLoading = true;
        _statsCts?.Dispose();
        _statsCts = new CancellationTokenSource();

        var cancellationToken = _statsCts.Token;
        var revision = _session.Revision;

        try
        {
            // 三条并行：互不依赖，串起来会让下拉框多等两个往返。
            var metadataTask = _api.GetAccountMetadataAsync(cancellationToken);
            var playTask = _api.GetAccountPlayDataAsync(cancellationToken);
            var vipTask = _api.GetAccountVipInfoAsync(cancellationToken);
            await Task.WhenAll(metadataTask, playTask, vipTask).ConfigureAwait(true);

            if (revision != _session.Revision)
            {
                // 期间换号了，这次结果作废。
                return;
            }

            _metadata = metadataTask.Result ?? _metadata;
            _playData = playTask.Result ?? _playData;
            _vip = vipTask.Result ?? _vip;

            RaiseStatsChanged();
        }
        catch (OperationCanceledException)
        {
            // 换号 / 登出导致的取消，静默。
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "账号统计读取失败");
        }
        finally
        {
            _statsLoading = false;
        }
    }

    /// <summary>
    /// 退出登录。
    /// </summary>
    /// <remarks>
    /// 这里只管清会话，跳转由宿主负责 —— 登出会让 <c>AccountChanged</c> 触发，
    /// 主窗口收到后把页面切回登录页。不在这里重复导航，避免两处同时切页。
    /// </remarks>
    [RelayCommand]
    private void SignOut() => _login.SignOut();

    private void OnAccountChanged(object? sender, EventArgs e)
    {
        // 换取会话的最后一步（BodianLogin.Adopt → RaiseAccountChanged）在线程池上完成，
        // 这个事件会从非 UI 线程过来；而这里发的 PropertyChanged 被 MainWindow 的 x:Bind 同步消费，
        // 会去碰 ToolTipService 这类 UI 对象。WinUI 3 跨线程访问直接抛 0x8001010E，
        // 且它发生在凭据落盘之后 —— 表现为「弹窗卡住，但重启后已经登录」。
        if (_dispatcher is { HasThreadAccess: false } dispatcher)
        {
            if (!dispatcher.TryEnqueue(() => OnAccountChanged(sender, e)))
            {
                _logger.LogWarning("账号变更无法投递回 UI 线程，标题栏账号信息不会更新");
            }

            return;
        }

        // 换号 / 登出：上一份统计不能再给新账号看，就地清空。
        // 不在这里拉 —— 拉取由下拉框打开驱动，见 RefreshStatsAsync。
        _statsCts?.Cancel();
        _metadata = null;
        _playData = null;
        _vip = null;

        OnPropertyChanged(nameof(AccountText));
        OnPropertyChanged(nameof(Avatar));
        OnPropertyChanged(nameof(IsVip));
        OnPropertyChanged(nameof(VipKindLabel));
        OnPropertyChanged(nameof(VipExpiryText));
        OnPropertyChanged(nameof(VipExpiryVisibility));
        RaiseStatsChanged();
    }

    private void RaiseStatsChanged()
    {
        OnPropertyChanged(nameof(IsVip));
        OnPropertyChanged(nameof(VipKindLabel));
        OnPropertyChanged(nameof(VipExpiryText));
        OnPropertyChanged(nameof(VipExpiryVisibility));
        OnPropertyChanged(nameof(StandardVipVisibility));
        OnPropertyChanged(nameof(BigVipVisibility));
        OnPropertyChanged(nameof(WelfareVipVisibility));
        OnPropertyChanged(nameof(FollowedArtistCountText));
        OnPropertyChanged(nameof(ListenTimeText));
        OnPropertyChanged(nameof(StatsVisibility));
    }

    private Visibility VisibleFor(VipBadgeKind kind) => Visible(Badge == kind);

    private static Visibility Visible(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;
}
