namespace Bodian.Core.Models;

/// <summary>
/// 界面上的一首歌。搜索列表与曲目详情共用。
/// </summary>
/// <remarks>
/// <para>
/// <b>只映射 P2 真正会用到的字段。</b> 底层 <c>TrackDto</c> 有六十多个字段，其中歌词轨
/// （P4）、搜索角标、社交计数、MV 相关、付费位等一律不进来 —— 没有消费方的字段搬上来只是噪音，
/// 而且会诱导后来的人「先填上，以后可能要用」。
/// </para>
/// <para>
/// <see cref="AvailableQualities"/> 在映射阶段就完成了「曲目级」的档位过滤：只保留本项目
/// 能播的三档、已去重、由高到低。代价是 UI 无法告诉用户「这首歌有母带但本客户端播不了」，
/// 记为已知限制。
/// </para>
/// </remarks>
public sealed record Track
{
    /// <summary>musicId。真实值会超出 int32，所以是 <see cref="long"/>。</summary>
    public required long Id { get; init; }

    /// <summary>曲名。</summary>
    public required string Title { get; init; }

    /// <summary>
    /// 展示用的艺人串。取自响应的 <c>artist</c> 拼接串；它缺失时由 <see cref="Artists"/> 拼。
    /// </summary>
    public string ArtistText { get; init; } = "";

    /// <summary>艺人明细。可能为空数组。</summary>
    public IReadOnlyList<TrackArtist> Artists { get; init; } = [];

    /// <summary>专辑名。</summary>
    public string? AlbumName { get; init; }

    /// <summary>封面地址。优先取 120px 那张，缺了才用大图。</summary>
    public Uri? CoverImage { get; init; }

    /// <summary>时长。底层响应的单位是**秒**。</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>这首歌在本项目可播的档位，已去重、由高到低。</summary>
    public IReadOnlyList<AudioQuality> AvailableQualities { get; init; } = [];

    /// <summary>
    /// 服务端标记这首歌需要 VIP。
    /// </summary>
    /// <remarks>
    /// <b>只用于界面展示，不得用于任何权限判断</b> —— 能不能播永远由 <c>checkRight</c> 裁决，
    /// 客户端的离线推断一律不算数。
    /// <para>
    /// <b>判据尚未闭环验证</b>：现有样本（5 条）全是 <c>feeType.vip == "1"</c> 的付费曲，
    /// 没有免费曲作对照，所以「<c>vip=0</c> 就是免费」这一步还是推断。
    /// 采到免费曲样本后要回来核对。
    /// </para>
    /// </remarks>
    public bool RequiresVip { get; init; }

    /// <summary>服务端标记这首歌需要单曲或专辑购买。同样只用于展示。</summary>
    public bool RequiresPurchase { get; init; }

    /// <summary>是否有本项目可播的档位。为 <c>false</c> 时只能试听或不可播。</summary>
    public bool HasPlayableQuality => AvailableQualities.Count > 0;
}
