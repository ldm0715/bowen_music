using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 专辑详情页。
/// </summary>
/// <remarks>
/// <para>
/// <b>两次请求</b>：一次取专辑信息（简介、发行日），一次取曲目。
/// 列表页点进来时已经知道专辑名与封面了，所以头部先显示得出来，
/// 简介那部分要等详情回来 —— 少一次「白屏等」。
/// </para>
/// <para>
/// 曲目**可分页**（<c>pn</c> 从 0），所以用 <see cref="PagedList{T}"/>；
/// 而详情是一次给全的，不走分页。
/// </para>
/// </remarks>
public sealed partial class AlbumDetailViewModel : ObservableObject
{
    private readonly IBodianApi _api;
    private readonly PlaybackCoordinator _coordinator;
    private readonly ILogger<AlbumDetailViewModel> _logger;

    private bool _infoLoaded;

    public AlbumDetailViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        Album album,
        ILogger<AlbumDetailViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(album);

        _api = api;
        _coordinator = coordinator;
        _logger = logger ?? NullLogger<AlbumDetailViewModel>.Instance;

        Album = album;

        Tracks = new PagedList<Track>(
            (cursor, token) => api.GetAlbumTracksAsync(album.Id, cursor, token),
            _logger,
            $"专辑「{album.Name}」",
            "这张专辑暂时取不到曲目。",
            Bodian.Core.Api.Paging.PagingConvention.ZeroBased);
    }

    /// <summary>列表页带过来的专辑（名字与封面已经是对的）。</summary>
    public Album Album { get; }

    public PagedList<Track> Tracks { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "";

    /// <summary>专辑简介。**实测很长**（整篇企划文案），界面上折叠显示。</summary>
    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial bool HasDescription { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        await LoadInfoAsync(cancellationToken).ConfigureAwait(true);
        await Tracks.EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>点播。队列就是这张专辑，所以「下一首」在专辑内有效。</summary>
    public async Task PlayAsync(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var index = Tracks.Items.IndexOf(track);

        if (index < 0)
        {
            return;
        }

        await _coordinator.PlayFromAsync([.. Tracks.Items], index).ConfigureAwait(true);
    }

    /// <summary>
    /// 取详情（简介 / 发行日 / 收藏数）。
    /// </summary>
    /// <remarks>
    /// <b>失败不挡曲目列表</b>：简介拿不到只是少一段文字，
    /// 让整页都加载不出来是过度反应。所以这里自己吞异常、只记日志。
    /// </remarks>
    private async Task LoadInfoAsync(CancellationToken cancellationToken)
    {
        if (_infoLoaded)
        {
            return;
        }

        _infoLoaded = true;
        IsBusy = true;

        // 先用列表页带来的信息把头部填上，请求回来再覆盖。
        Title = Album.Name;
        Subtitle = BuildSubtitle(Album);

        try
        {
            var detail = await _api.GetAlbumAsync(Album.Id, cancellationToken).ConfigureAwait(true);

            if (detail is null)
            {
                return;
            }

            Title = detail.Name;
            Subtitle = BuildSubtitle(detail);
            Description = detail.Description;
            HasDescription = detail.HasDescription;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载专辑 {AlbumId} 详情失败", Album.Id);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary><c>艺人 · 2003-07-31 · 11 首</c>，缺的部分自动省掉。</summary>
    private static string BuildSubtitle(Album album)
    {
        var parts = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(album.ArtistText))
        {
            parts.Add(album.ArtistText);
        }

        if (!string.IsNullOrWhiteSpace(album.ReleaseDate))
        {
            parts.Add(album.ReleaseDate);
        }

        if (album.MusicCount > 0)
        {
            parts.Add($"{album.MusicCount} 首");
        }

        return string.Join(" · ", parts);
    }
}
