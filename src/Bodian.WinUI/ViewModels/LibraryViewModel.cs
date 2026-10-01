using System.Collections.ObjectModel;
using Bodian.Core.Api;
using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 乐库页。
/// </summary>
/// <remarks>
/// <para>
/// <b>整页只要一次请求</b>：<c>play/music/library/navigation</c> 一次带回 15 个大类、
/// 它们的简介与封面、以及每类的子类（子类还各带一张代表专辑）。
/// 所以这里没有懒加载、没有分页。
/// </para>
/// <para>
/// <b>这不是「歌单广场」。</b> 那套是 <c>service/category/*</c>（用户创建的歌单），
/// 与乐库是两个功能 —— 本项目一度搞混过，见 <c>reverse/findings/07-musiclib.md</c>。
/// </para>
/// <para>
/// <b>子类的完整专辑列表还没接通</b>：要调 <c>play/music/library/albums</c>，
/// 而它的 <c>pTypeId</c>/<c>cTypeId</c> 取值尚未确定。本版先把 navigation 顺手带的那张
/// <see cref="MusicCategoryChild.SampleAlbum"/> 画出来。
/// </para>
/// </remarks>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly IBodianApi _api;
    private readonly ILogger<LibraryViewModel> _logger;

    private bool _loaded;

    public LibraryViewModel(IBodianApi api, ILogger<LibraryViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _logger = logger ?? NullLogger<LibraryViewModel>.Instance;
    }

    /// <summary>15 个大类。顺序就是服务端给的顺序（按 priority 排好的）。</summary>
    public ObservableCollection<MusicCategoryGroup> Groups { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    /// <summary>首次进入时调。<b>已经加载过就什么都不做。</b></summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        IsBusy = true;
        StatusText = "正在加载乐库…";

        try
        {
            var groups = await _api.GetMusicLibraryAsync(cancellationToken).ConfigureAwait(true);

            Groups.Clear();

            foreach (var group in groups)
            {
                Groups.Add(group);
            }

            var children = Groups.Sum(group => group.Children.Count);

            StatusText = Groups.Count == 0
                ? "没有取到分类。"
                : $"{Groups.Count} 个大类、{children} 个子类";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载乐库失败");

            // 失败要允许重试：把标志放回去，下次进来会再试一次。
            _loaded = false;
            StatusText = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
