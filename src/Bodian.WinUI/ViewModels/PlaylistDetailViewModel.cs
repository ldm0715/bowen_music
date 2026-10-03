using System.Globalization;
using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.WinUI.Playback;
using Bodian.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 歌单详情页。
/// </summary>
/// <remarks>
/// <para>
/// 歌单对象由调用方传进来（侧栏本来就持有那份列表），头部先用它填上，再打一次
/// <c>service/playlist/info/{id}</c> 补详情 —— 播放数、创建者、简介只有那个响应有。
/// </para>
/// <para>
/// <b><c>source</c> 必须由调用方给</b>：侧栏里的自建歌单是 <c>5</c>，
/// 而发现页点进来的**公开歌单是 4 或 13**。填错的表现是「曲目列表是空的」——
/// 服务端对不上的 <c>source</c> 只回空、不报错。
/// </para>
/// <para>
/// <b>这个值同时对两件事生效</b>：取曲目、以及判断这个歌单是不是自己创建的（见
/// <see cref="IsOwnPlaylist"/>）。所以它不能为了「看起来对」而改写。
/// </para>
/// </remarks>
public sealed partial class PlaylistDetailViewModel : PlaylistTracksViewModel
{
    /// <summary>账号歌单（自建与「我喜欢」）的 <c>source</c>，与 <c>BodianApi</c> 里那个私有常量同值。</summary>
    /// <remarks>
    /// <c>BodianApi</c> 那个是 <c>private</c>，这里只能各写一份。改动时两处要一起改。
    /// </remarks>
    private const int AccountPlaylistSource = 5;

    private readonly int _source;
    private readonly IBodianApi _api;
    private readonly BodianSession _session;
    private readonly IClipboardService _clipboard;
    private readonly INoticeSink _notice;
    private readonly IPlaylistLibrarySink _library;
    private readonly ILogger<PlaylistDetailViewModel> _logger;

    private bool _infoLoaded;

    /// <summary>详情响应。收藏/取消后靠它就地重算副标题，**不为一个计数再打一次请求**。</summary>
    private Playlist? _info;

    public PlaylistDetailViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        BodianSession session,
        IClipboardService clipboard,
        Playlist playlist,
        int source,
        INoticeSink notice,
        IPlaylistLibrarySink library,
        ILogger<PlaylistDetailViewModel>? logger = null)
        : base(api, coordinator, logger ?? NullLogger<PlaylistDetailViewModel>.Instance)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(library);

        _api = api;
        _session = session;
        _clipboard = clipboard;
        _notice = notice;
        _library = library;
        _logger = logger ?? NullLogger<PlaylistDetailViewModel>.Instance;
        Playlist = playlist;
        _source = source;

        // 头部先用手上这份填上，详情回来再覆盖 —— 少一次「白屏等」（同专辑详情页）。
        Title = playlist.Name;
        CoverImage = playlist.CoverImage;

        // 归属要在拿到详情之前先算一次：靠 source 那一条判据不依赖网络，
        // 否则自己的歌单会先闪一下收藏按钮再消失。
        ComputeOwnership(null);
    }

    /// <summary>点进来的那个歌单。也用它的 id 作为导航身份的一部分。</summary>
    public Playlist Playlist { get; }

    /// <summary>
    /// 这个歌单的来源。**同时是取曲目的参数与导航身份的一部分** ——
    /// 同一个 id 在不同 source 下是不同的歌单，只按 id 判等会让两者互相顶掉。
    /// </summary>
    public int Source => _source;

    /// <inheritdoc />
    protected override int PlaylistSource => _source;

    // ── 头部 ────────────────────────────────────────────────────────────

    /// <summary>封面。列表页带过来的那份就是对的，详情回来后可能被覆盖。</summary>
    [ObservableProperty]
    public partial Uri? CoverImage { get; set; }

    /// <summary><c>177 首 · 565w7+ 播放 · 2.1w 收藏</c>，缺的部分自动省掉。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMeta))]
    [NotifyPropertyChangedFor(nameof(ShowStatusText))]
    public partial string Subtitle { get; set; } = "";

    /// <summary>歌单简介。**实测可能很长**（整篇企划文案），界面上折叠显示。</summary>
    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial bool HasDescription { get; set; }

    /// <summary>创建者那一行要不要出现。</summary>
    [ObservableProperty]
    public partial bool HasCreator { get; set; }

    [ObservableProperty]
    public partial string CreatorName { get; set; } = "";

    [ObservableProperty]
    public partial Uri? CreatorCover { get; set; }

    /// <summary>副标题拼出来了没有。没拼出来时那行整个不显示，不留一行空白。</summary>
    public bool HasMeta => Subtitle.Length > 0;

    /// <summary>
    /// 状态行要不要显示 —— 只在副标题没拼出来时显示。
    /// </summary>
    /// <remarks>
    /// 副标题里的「N 首」与状态行的「N 首」是同一件事，两个都显示就是重复。
    /// 但状态行还是不能删：它同时承担「加载失败：…」与「这个歌单里还没有歌」，
    /// 那两条消息在详情拿不到时是唯一的出口。
    /// </remarks>
    public bool ShowStatusText => Subtitle.Length == 0;

    // ── 归属 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 这个歌单是不是我自己创建的。
    /// </summary>
    /// <remarks>
    /// 两条判据取并集，理由见 <see cref="ComputeOwnership"/>。
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCollect))]
    public partial bool IsOwnPlaylist { get; set; }

    /// <summary>
    /// 收藏按钮要不要显示 —— **自己的歌单不显示**。
    /// </summary>
    /// <remarks>
    /// 收藏自己的歌单没有意义，挂在那儿只是给人误点的机会。
    /// </remarks>
    public bool CanCollect => !IsOwnPlaylist;

    // ── 收藏 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 这个歌单当前是否已被我收藏。
    /// </summary>
    /// <remarks>
    /// <c>null</c> = 还没判定出来（详情没回来或读取失败），按钮按「未收藏」显示。
    /// 判据是歌单详情里的 <c>collectTime</c>，**不是 <c>isFond</c>** ——
    /// 见 <c>reverse/findings/13-collect-playlist-follow-artist.md</c> §3.3。
    /// </remarks>
    [ObservableProperty]
    public partial bool? IsCollected { get; set; }

    /// <summary>收藏/取消正在进行。<b>挡住重复点击</b> —— 服务端把 <c>op</c> 当一次设置，重发会打架。</summary>
    [ObservableProperty]
    public partial bool IsCollectBusy { get; set; }

    /// <summary>删除正在进行。<b>挡住重复点击</b> —— 删除不可逆，不能发第二次。</summary>
    [ObservableProperty]
    public partial bool IsDeleteBusy { get; set; }

    /// <summary>「播放全部」正在把剩下的页拉完。与 <see cref="PlaylistTracksViewModel.IsBusy"/> 分开，两者不是一件事。</summary>
    [ObservableProperty]
    public partial bool IsPlayingAll { get; set; }

    /// <summary>
    /// 歌单取不到曲目时的说明。
    /// </summary>
    /// <remarks>
    /// 自建歌单是可以被删掉的，而侧栏那份列表是启动时拉的 —— 期间用户在别处删了歌单，
    /// 点进来就会是空的。这条文案说的是「找不到」，不是「加载失败」。
    /// </remarks>
    protected override string MissingText => "这个歌单不存在，可能已经被删除了。";

    protected override string EmptyText => "这个歌单里还没有歌。";

    /// <summary>页面进入时调一次：曲目列表 + 详情（含收藏态）。</summary>
    public async Task EnsureDetailLoadedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);
        await LoadInfoAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// 播放全部。
    /// </summary>
    /// <remarks>
    /// <b>先把剩余的页拉完再播</b>：歌单首屏只回来一页，直接拿已加载的那几条排队列的话，
    /// 177 首的歌单点一次只播得到首屏那些。拉不完也照常从第一首开始播 ——
    /// 队列短一点比什么都不播好。
    /// </remarks>
    [RelayCommand]
    private async Task PlayAllAsync()
    {
        if (IsPlayingAll)
        {
            return;
        }

        IsPlayingAll = true;

        try
        {
            await LoadAllAsync().ConfigureAwait(true);

            if (Tracks.Count > 0)
            {
                await Coordinator.PlayFromAsync([.. Tracks], 0).ConfigureAwait(true);
            }
        }
        finally
        {
            IsPlayingAll = false;
        }
    }

    /// <summary>
    /// 复制歌单分享链接。
    /// </summary>
    /// <remarks>
    /// <b><c>source</c> 要原样带上</b>：歌单的分享模板里它是必需参数
    /// （<c>ShareLinks.BuildPlaylistLink</c>），写死 4 会得到一条打不开的链接。
    /// <b>不做上报</b>：<c>service/share/text</c> 的 <c>shareSource</c> 只实测过歌曲与歌手，
    /// 歌单取什么值没有证据，所以不猜 —— 代价是这里的分享不会让服务端分享数 +1。
    /// </remarks>
    [RelayCommand]
    private void Share()
    {
        try
        {
            _clipboard.SetText(ShareLinks.BuildPlaylistLink(Playlist.Id, _session.Uid, _source));
            _notice.Show("链接已复制");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "复制歌单 {PlaylistId} 分享链接失败", Playlist.Id);
            _notice.Show("复制链接失败");
        }
    }

    /// <summary>
    /// 收藏 / 取消收藏，并就地更新按钮状态。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>写成功后不重拉详情。</b> 全局收藏数是个累计值，服务端多半有延迟，重拉一次要等一个来回
    /// 还可能拿到旧数；而它在界面上是缩写过的（<c>2w1+</c>），±1 根本看不出来。
    /// 真正要立刻正确的是按钮状态，本地已经知道了。所以这里只把本地那份计数 ±1 再重拼一次副标题 ——
    /// 小歌单（十几收藏）下这一下是看得见的，大歌单下与不更新没有区别。
    /// </para>
    /// <para>
    /// <b>确认弹窗不在这里</b>：那是界面决策（要不要弹、按钮怎么摆），且 <c>XamlRoot</c>
    /// 拿不到 ViewModel 里来。取消操作由页面在调用前先确认，见
    /// <c>PlaylistDetailPage.OnCollectClick</c>。
    /// </para>
    /// </remarks>
    public async Task SetCollectedAsync(bool collected, CancellationToken cancellationToken = default)
    {
        if (IsCollectBusy)
        {
            return;
        }

        IsCollectBusy = true;
        try
        {
            await _api.SetPlaylistCollectedAsync(Playlist.Id, _source, collected, cancellationToken)
                .ConfigureAwait(true);

            IsCollected = collected;
            BumpCollectedCount(collected);
            _notice.Show(collected ? "已收藏" : "已取消收藏");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "歌单 {PlaylistId} {Operation}失败", Playlist.Id, collected ? "收藏" : "取消收藏");
            _notice.Show(collected ? "收藏失败" : "取消收藏失败");
        }
        finally
        {
            IsCollectBusy = false;
        }
    }

    /// <summary>
    /// 删掉这个歌单。成功返回 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>确认框不在这里</b>：那是界面决策（要不要弹、按钮怎么摆），<c>XamlRoot</c>
    /// 也拿不到 ViewModel 里来 —— 与取消收藏同一条规矩，见
    /// <c>PlaylistDetailPage.OnDeletePlaylistClick</c>。
    /// </para>
    /// <para>
    /// <b>删完不自己导航</b>：这一页是从侧栏换根进来的**根页**，删掉之后去哪由外壳决定 ——
    /// 它还要同时把侧栏里那一行摘掉。所以这里只通知 <see cref="IPlaylistLibrarySink"/>。
    /// </para>
    /// </remarks>
    public async Task<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (IsDeleteBusy)
        {
            return false;
        }

        IsDeleteBusy = true;
        try
        {
            await _api.DeletePlaylistAsync(Playlist.Id, cancellationToken).ConfigureAwait(true);

            _logger.LogInformation("已删除歌单 {PlaylistId}", Playlist.Id);

            _library.OnPlaylistRemoved(Playlist.Id);
            _notice.Show($"已删除「{Title}」");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "删除歌单 {PlaylistId} 失败", Playlist.Id);
            _notice.Show("删除失败，请稍后再试。");
            return false;
        }
        finally
        {
            IsDeleteBusy = false;
        }
    }

    // ── 编辑 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 编辑正在进行。<b>挡住重复提交</b> —— 上传 + 编辑是两次写请求，重入会打架。
    /// </summary>
    [ObservableProperty]
    public partial bool IsEditBusy { get; set; }

    /// <summary>
    /// 封面回传用的**原始串**（<c>PUT</c> 的 <c>pic</c>）。
    /// </summary>
    /// <remarks>
    /// 不改封面时必须把它原样回传 —— <see cref="CoverImage"/> 是规范化过的 <c>Uri</c>，
    /// 回传改写过的地址是否被接受没有验证过。详情回来了就用详情那份（更全）。
    /// </remarks>
    public string CoverRawUrl => _info?.CoverRawUrl ?? Playlist.CoverRawUrl;

    /// <summary>
    /// 歌单当前的标签，编辑时用来预选。
    /// </summary>
    /// <remarks>
    /// <b>只有详情会给标签</b>（<c>service/playlist/info</c> 的 <c>categories</c>），
    /// 列表来源没有 —— 所以详情还没回来时这里就是空的，与「这个歌单没有标签」无法区分。
    /// 编辑入口只在 <see cref="IsOwnPlaylist"/> 时出现，而那一页必然会加载详情。
    /// </remarks>
    public IReadOnlyList<MusicCategory> Categories => _info?.Categories ?? Playlist.Categories;

    /// <summary>
    /// 标签的候选项。与「歌单广场」用的是同一棵树（<c>service/category/list</c>）。
    /// </summary>
    /// <remarks>匿名也能调，但只有登录后才有意义 —— 没登录就没有编辑入口。</remarks>
    public Task<IReadOnlyList<CategoryGroup>> LoadCategoriesAsync(CancellationToken cancellationToken = default)
        => _api.GetCategoriesAsync(cancellationToken);

    /// <summary>
    /// 保存编辑。成功返回 <c>true</c>，并就地刷新头部与侧栏那一行。
    /// </summary>
    /// <param name="name">新名字。空白由 <c>BodianApi</c> 挡（它会抛）。</param>
    /// <param name="description">新简介。</param>
    /// <param name="coverRawUrl">当前封面原始串；<paramref name="coverBytes"/> 非空时会被上传结果覆盖。</param>
    /// <param name="categories">选中的标签，最多 3 个。</param>
    /// <param name="coverBytes">新封面的字节；<c>null</c> 表示没换封面。</param>
    /// <remarks>
    /// <para>
    /// <b>换封面是两步</b>：先 <c>uploadPic</c> 拿地址，再把这个地址填进 <c>PUT</c> 的 <c>pic</c> ——
    /// 上传本身不会换掉封面。两步都成功才算改完。
    /// </para>
    /// <para>
    /// <b>确认框不在这里</b>：与删除同一条规矩，弹窗是界面决策，<c>XamlRoot</c> 也拿不到 VM 里来。
    /// </para>
    /// <para>
    /// <b>失败不改任何本地状态</b>：头部与侧栏都保持原样，用户看到的还是服务端那份。
    /// </para>
    /// </remarks>
    public async Task<bool> SaveEditAsync(
        string name,
        string description,
        string coverRawUrl,
        IReadOnlyList<MusicCategory> categories,
        byte[]? coverBytes,
        string coverFileName,
        string coverContentType,
        CancellationToken cancellationToken = default)
    {
        if (IsEditBusy)
        {
            return false;
        }

        IsEditBusy = true;

        try
        {
            var pic = coverRawUrl;

            if (coverBytes is { Length: > 0 })
            {
                pic = await _api
                    .UploadPlaylistCoverAsync(Playlist.Id, coverBytes, coverFileName, coverContentType, cancellationToken)
                    .ConfigureAwait(true);
            }

            var trimmed = name.Trim();
            var ids = categories.Select(c => (int)c.Id).ToArray();

            await _api.UpdatePlaylistAsync(Playlist.Id, trimmed, description, pic, ids, cancellationToken)
                .ConfigureAwait(true);

            ApplyEdit(trimmed, description, pic, categories);
            _library.OnPlaylistUpdated(Playlist.Id, trimmed, TryCreateHttpUri(pic));
            _notice.Show("已保存");

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "编辑歌单 {PlaylistId} 失败", Playlist.Id);
            _notice.Show("歌单编辑失败，请稍后再试。");
            return false;
        }
        finally
        {
            IsEditBusy = false;
        }
    }

    /// <summary>
    /// 把编辑结果落到头部绑定字段上。
    /// </summary>
    /// <remarks>
    /// <b>不能重新赋值 <see cref="Playlist"/></b>：它是 <c>get</c>-only 的记录，而且是
    /// 导航身份的一部分（改了会让同一个页面被判成另一个页面）。所以只动绑定的
    /// <c>[ObservableProperty]</c>，以及那份详情副本 <c>_info</c> —— 副标题靠它重算。
    /// </remarks>
    private void ApplyEdit(string name, string description, string pic, IReadOnlyList<MusicCategory> categories)
    {
        var cover = TryCreateHttpUri(pic);

        Title = name;
        Description = description;
        HasDescription = !string.IsNullOrWhiteSpace(description);

        if (cover is not null)
        {
            CoverImage = cover;
        }

        if (_info is null)
        {
            return;
        }

        _info = _info with
        {
            Name = name,
            Description = description,
            CoverRawUrl = pic,
            CoverImage = cover ?? _info.CoverImage,
            Categories = categories,
        };

        Subtitle = BuildSubtitle(_info);
    }

    /// <summary>把封面串转成可用的地址。空串或非法地址返回 <c>null</c>（表示「没有封面」）。</summary>
    private static Uri? TryCreateHttpUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;

    protected override Task<Playlist?> ResolvePlaylistAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Playlist?>(Playlist);

    /// <summary>
    /// 取歌单详情：头部元数据与收藏态来自**同一次请求**。
    /// </summary>
    /// <remarks>
    /// <b>失败不挡曲目列表</b>：详情拿不到只是头部少几项，
    /// 让整页都加载不出来是过度反应。所以这里自己吞异常、只记日志。
    /// </remarks>
    private async Task LoadInfoAsync(CancellationToken cancellationToken)
    {
        if (_infoLoaded)
        {
            return;
        }

        _infoLoaded = true;

        try
        {
            var info = await _api.GetPlaylistInfoAsync(Playlist.Id, _source, cancellationToken)
                .ConfigureAwait(true);

            if (info is null)
            {
                // 详情是空对象（source 对不上，或这个来源没有详情）。头部退回构造函数里
                // 用调用方带来的那份填过的名字与封面，副标题拼不出来 → 状态行顶上。
                // 归属仍要算一遍：source == 5 那条兜底不依赖网络结果。
                ComputeOwnership(null);
                return;
            }

            _info = info;
            CoverImage = info.CoverImage ?? CoverImage;
            Title = info.Name;
            Subtitle = BuildSubtitle(info);
            Description = info.Description;
            HasDescription = info.HasDescription;
            CreatorName = info.CreatorName;
            CreatorCover = info.CreatorCover;
            IsCollected = info.IsCollected;
            ComputeOwnership(info);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载歌单 {PlaylistId} 详情失败", Playlist.Id);
            ComputeOwnership(null);
        }
    }

    /// <summary>
    /// 判断这个歌单是不是自己创建的。
    /// </summary>
    /// <param name="info">详情响应；<c>null</c> 表示没拿到。</param>
    /// <remarks>
    /// <para>
    /// 两条判据取<b>并集</b>：
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// 详情给了 <c>creatorId</c> 且就是当前账号 uid —— 这条能覆盖「从搜索结果点进自己创建的
    /// 公开歌单」，那条路径的 <c>source</c> 不是 5，只有 creatorId 认得出。
    /// </description></item>
    /// <item><description>
    /// 调用方给的就是账号歌单的 <c>source</c>（5）—— 侧栏「创建的歌单」与账号歌单清单都传 5，
    /// 且只有它们传 5。这条不依赖任何网络结果，详情拿不到时仍然成立。
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>两条都不成立时按「别人的歌单」处理</b>（显示收藏按钮）。走到这一步的只可能是
    /// 发现页 / 收藏列表 / 搜索点进来的，那里藏掉按钮会让这个页面的主要用途没有入口；
    /// 而唯一确定的「自己的」入口已经被第 2 条兜住了。
    /// </para>
    /// </remarks>
    private void ComputeOwnership(Playlist? info)
    {
        var matchesCurrentUser = _session.IsAuthenticated
            && info is { CreatorId: > 0 }
            && long.TryParse(_session.Uid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid)
            && uid == info.CreatorId;

        IsOwnPlaylist = matchesCurrentUser || _source == AccountPlaylistSource;

        // 自己的歌单不显示创建者 —— 那一行只会写着「我」，是噪音。
        HasCreator = !IsOwnPlaylist && info is { HasCreator: true };
    }

    /// <summary>
    /// 收藏成功后把本地那份全局收藏数挪一格，并重拼副标题。
    /// </summary>
    /// <remarks>
    /// 只动本地副本，不回写 <see cref="Playlist"/>（那是调用方传进来的、也是导航身份的一部分）。
    /// 详情还没回来（<c>_info</c> 是 null）时什么都不做 —— 那时副标题本来就没拼出来。
    /// </remarks>
    private void BumpCollectedCount(bool collected)
    {
        if (_info is not { CollectedCount: > 0 } info)
        {
            return;
        }

        _info = info with { CollectedCount = collected ? info.CollectedCount + 1 : info.CollectedCount - 1 };
        Subtitle = BuildSubtitle(_info);
    }

    /// <summary>
    /// <c>177 首 · 565w7+ 播放 · 2.1w 收藏</c>，缺的部分自动省掉。
    /// </summary>
    /// <remarks>
    /// 大数走 <see cref="CommentCountLabel.Format"/>，与歌手页头部的粉丝数是同一套缩写 ——
    /// 同一屏里出现两种「w 缩写」才是真的乱。它的规则是超过一万后取到千位并加 <c>+</c>，
    /// 所以 5657990 是 <c>565w7+</c> 而不是 <c>565.8w</c>。
    /// </remarks>
    private static string BuildSubtitle(Playlist playlist)
    {
        var parts = new List<string>(3);

        if (playlist.MusicCount > 0)
        {
            parts.Add($"{playlist.MusicCount} 首");
        }

        if (playlist.PlayCount > 0)
        {
            parts.Add($"{CommentCountLabel.Format(playlist.PlayCount)} 播放");
        }

        if (playlist.CollectedCount > 0)
        {
            parts.Add($"{CommentCountLabel.Format(playlist.CollectedCount)} 收藏");
        }

        return string.Join(" · ", parts);
    }
}
