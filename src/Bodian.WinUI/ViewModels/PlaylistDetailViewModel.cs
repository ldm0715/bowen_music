using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.WinUI.Playback;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 歌单详情页。
/// </summary>
/// <remarks>
/// <para>
/// 歌单对象由调用方传进来（侧栏本来就持有那份列表），所以这里**不需要再查一次歌单**，
/// 直接拿 <see cref="Playlist.Id"/> 去取曲目。
/// </para>
/// <para>
/// <b><c>source</c> 必须由调用方给</b>：侧栏里的自建歌单是 <c>5</c>，
/// 而发现页点进来的**公开歌单是 4**。填错的表现是「曲目列表是空的」——
/// 服务端对不上的 <c>source</c> 只回空、不报错。
/// </para>
/// </remarks>
public sealed partial class PlaylistDetailViewModel : PlaylistTracksViewModel
{
    private readonly int _source;

    public PlaylistDetailViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        Playlist playlist,
        int source,
        ILogger<PlaylistDetailViewModel>? logger = null)
        : base(api, coordinator, logger ?? NullLogger<PlaylistDetailViewModel>.Instance)
    {
        ArgumentNullException.ThrowIfNull(playlist);

        Playlist = playlist;
        _source = source;
        Title = playlist.Name;
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

    /// <summary>
    /// 歌单取不到曲目时的说明。
    /// </summary>
    /// <remarks>
    /// 自建歌单是可以被删掉的，而侧栏那份列表是启动时拉的 —— 期间用户在别处删了歌单，
    /// 点进来就会是空的。这条文案说的是「找不到」，不是「加载失败」。
    /// </remarks>
    protected override string MissingText => "这个歌单不存在，可能已经被删除了。";

    protected override string EmptyText => "这个歌单里还没有歌。";

    protected override Task<Playlist?> ResolvePlaylistAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Playlist?>(Playlist);
}
