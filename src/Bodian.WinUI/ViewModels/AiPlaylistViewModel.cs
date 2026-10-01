using System.Collections.ObjectModel;
using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Models.Home;
using Bodian.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 一个 AI 歌单（发现页「个性化歌单」里点进来的那一组）。
/// </summary>
/// <remarks>
/// <para>
/// <b>没有分页</b>：<c>service/home/aiPlaylistDetail?index=N</c> 一次给全（实测 30 首），
/// 所以这里不用 <c>PagedCursor</c>，也不用 <c>PagedList</c>。
/// </para>
/// <para>
/// 标题在进来之前就能给（模块里那一组的标题与详情响应的 <c>title</c> 实测逐字相同），
/// 所以构造函数里先填上，详情回来后用它自己的值覆盖 —— 万一不一致，以服务端为准。
/// </para>
/// </remarks>
public sealed partial class AiPlaylistViewModel : ObservableObject
{
    private readonly IBodianApi _api;
    private readonly PlaybackCoordinator _coordinator;
    private readonly ILogger<AiPlaylistViewModel> _logger;

    private readonly AiPlaylistRef _target;

    public AiPlaylistViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        AiPlaylistRef target,
        string title,
        ILogger<AiPlaylistViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(target);

        _api = api;
        _coordinator = coordinator;
        _logger = logger ?? NullLogger<AiPlaylistViewModel>.Instance;

        _target = target;
        Title = title;
    }

    public ObservableCollection<Track> Tracks { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>副标题，形如「为你量身打造的专属歌单」。</summary>
    [ObservableProperty]
    public partial string Subtitle { get; set; } = "";

    /// <summary>大字标题，形如 <c>Daily Songs</c>。装饰用，缺了不显示。</summary>
    [ObservableProperty]
    public partial string BigTitle { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || Tracks.Count > 0)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在加载…";

        try
        {
            var playlist = await _api.GetAiPlaylistAsync(_target.Index, _target.PassRecName, cancellationToken)
                .ConfigureAwait(true);

            if (playlist is null)
            {
                StatusText = "这个歌单暂时取不到。";
                return;
            }

            // 服务端的标题优先：进来时那个是模块里的，理论上一致，但不一致时以服务端为准。
            if (!string.IsNullOrWhiteSpace(playlist.Title))
            {
                Title = playlist.Title;
            }

            Subtitle = playlist.Subtitle;
            BigTitle = playlist.BigTitle;

            foreach (var track in playlist.Tracks)
            {
                Tracks.Add(track);
            }

            StatusText = Tracks.Count == 0 ? "这个歌单里还没有歌。" : $"{Tracks.Count} 首";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 AI 歌单 {Target} 失败", _target);
            StatusText = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>点播。队列就是这个歌单，所以「下一首」在歌单内有效。</summary>
    public async Task PlayAsync(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var index = Tracks.IndexOf(track);

        if (index < 0)
        {
            return;
        }

        await _coordinator.PlayFromAsync([.. Tracks], index).ConfigureAwait(true);
    }
}
