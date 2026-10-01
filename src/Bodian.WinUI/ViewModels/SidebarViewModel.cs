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
    /// 拉取自建歌单。**可以重复调**（登录、切号之后都要重来）。
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
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
            // 会话没了就会走到这里（UidPair 抛 InvalidOperationException）。
            // 那不是异常情况：登出时外壳本来就会把页面切回登录页。
            _logger.LogWarning(ex, "拉取自建歌单失败");

            Playlists.Clear();
            ErrorText = ex.Message;
        }
    }
}
