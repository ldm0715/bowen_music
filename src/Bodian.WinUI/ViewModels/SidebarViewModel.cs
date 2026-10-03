using System.Collections.ObjectModel;
using Bodian.Core.Api;
using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 侧栏里「创建的歌单」那一段。
/// </summary>
/// <remarks>
/// <para>
/// <b>失败不抛，写进 <see cref="ErrorText"/>。</b> 侧栏是常驻的，一次网络抖动不该把外壳打崩；
/// 而且这里失败只影响「创建的歌单」这一段，其余导航项照常可用。
/// </para>
/// <para>
/// <b>这一段是启动时就要拉的</b>，因为它本身就是侧栏的内容（不拉就不知道有几项）。
/// 这与「不做启动时全量预取」不冲突 —— 那条说的是不要一次并发去拉六个页面。
/// </para>
/// </remarks>
public sealed partial class SidebarViewModel : ObservableObject
{
    private readonly IBodianApi _api;
    private readonly ILogger<SidebarViewModel> _logger;

    public SidebarViewModel(IBodianApi api, ILogger<SidebarViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _logger = logger ?? NullLogger<SidebarViewModel>.Instance;
    }

    /// <summary>自建歌单。侧栏按下标顺序逐条加进去。</summary>
    public ObservableCollection<Playlist> Playlists { get; } = [];

    /// <summary>拉取失败的原因。为 <c>null</c> 时界面不显示这一行。</summary>
    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    /// <summary>
    /// 新建失败的原因。
    /// </summary>
    /// <remarks>
    /// <b>刻意不复用 <see cref="ErrorText"/></b>：那个驱动「歌单加载失败，点击重试」那一行，
    /// 一次新建失败写进去，侧栏会显示成整段列表拉不到 —— 与事实不符。
    /// </remarks>
    [ObservableProperty]
    public partial string? CreateErrorText { get; set; }

    /// <summary>正在拉取。刷新按钮据此禁用，也挡住重复进入。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    public partial bool IsBusy { get; set; }

    /// <summary>刷新按钮可用。</summary>
    public bool CanRefresh => !IsBusy;

    /// <summary>
    /// 拉取自建歌单。**可以重复调**（登录、切号之后都要重来）。
    /// </summary>
    /// <remarks>
    /// 并发的第二次直接忽略而不是排队：两次请求目标相同，排队只会多打一次服务端，
    /// 还让先到的结果被后到的覆盖。
    /// </remarks>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorText = null;

        try
        {
            var playlists = await _api.GetCreatedPlaylistsAsync(cancellationToken).ConfigureAwait(true);

            Playlists.Clear();

            foreach (var playlist in playlists)
            {
                Playlists.Add(playlist);
            }
        }
        catch (Exception ex)
        {
            // 会话没了也会走到这里（UidPair 抛 InvalidOperationException）。
            // 那不是异常情况：登出时外壳本来就会把页面切回登录页。
            _logger.LogWarning(ex, "拉取自建歌单失败");

            // ★ 这里**不清空列表**。失败路径原来是 Clear() + 写 ErrorText，
            //   对启动时的自动拉取无害（本来就没东西），对用户主动点的刷新则是倒退 ——
            //   手上有数据，一次网络抖动就让列表变空。跨账号的清理交给 Reset()。
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>清空这一段。换号与登出时调，免得上一个账号的歌单留在新会话里。</summary>
    public void Reset()
    {
        Playlists.Clear();
        ErrorText = null;
        CreateErrorText = null;
    }

    /// <summary>
    /// 新建歌单。成功就把新歌单插到列表最前面；失败写 <see cref="CreateErrorText"/> 并返回 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>新行是本地拼的</b>：新建的回执只有 <c>id</c>（实测，文档 2.4），拿不到完整对象。
    /// 好在其余字段本来就是确定的 —— 新歌单没有封面、曲目数为 0，
    /// 而 <c>service/playlist/userCreate</c> 按 id 降序，新建的必然排在首位（实测）。
    /// 这样点完立刻看得到，不依赖一次额外的列表请求，也就没有「建成了但刷新失败」的中间态。
    /// </para>
    /// <para>
    /// <c>SourceType</c> 留 <c>0</c>：服务端没给。侧栏点进详情走的是固定的
    /// <c>SidebarPlaylistSource = 5</c>，不读这个字段，所以留空不会让详情页取不到曲目。
    /// </para>
    /// </remarks>
    public async Task<bool> CreateAsync(string name, bool isPrivate, CancellationToken cancellationToken = default)
    {
        CreateErrorText = null;

        try
        {
            var id = await _api.CreatePlaylistAsync(name, isPrivate, cancellationToken).ConfigureAwait(true);

            Playlists.Insert(0, new Playlist { Id = id, Name = name.Trim(), IsPrivate = isPrivate });

            _logger.LogInformation("已新建歌单 {Name}（id {Id}，隐私 {IsPrivate}）", name, id, isPrivate);

            return true;
        }
        catch (InvalidOperationException)
        {
            // 未登录，或请求在途时换了账号。没发请求就没有别的失败可能，所以单独认它。
            CreateErrorText = "登录后可以新建歌单。";
            return false;
        }
        catch (ArgumentException ex)
        {
            // Core 那边挡下来的空名字。
            CreateErrorText = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "新建歌单失败");

            CreateErrorText = "新建歌单失败，请稍后再试。";
            return false;
        }
    }
}
