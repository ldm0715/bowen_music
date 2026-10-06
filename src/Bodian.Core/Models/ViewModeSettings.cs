namespace Bodian.Core.Models;

/// <summary>
/// 「用封面卡片还是行列表」这一个界面偏好。
/// </summary>
/// <remarks>
/// <para>
/// 管的是**所有「一屏多条封面、靠封面认东西」的地方**：搜索结果的歌单 / 专辑 / 歌手三个页签，
/// 以及「收藏的专辑」「收藏的歌单」两页。行列表看得快、封面网格认得准，各有各的用处。
/// 曲目表格（单曲、歌单详情、专辑详情那些）不受这个开关影响。
/// </para>
/// <para>
/// <b>全局共用一个开关</b>，不按页面各记一份：它是「这个人习惯怎么看东西」，
/// 不是「专辑这一类结果天生该怎么排」。按页面分开的话，用户在一处切了卡片、
/// 换一处又是列表，只会觉得这个开关没生效。
/// </para>
/// </remarks>
public sealed record ViewModeSettings
{
    /// <summary>
    /// 是否用封面卡片而不是行列表。
    /// </summary>
    /// <remarks>
    /// <b>默认关（行列表）。</b> 卡片是后加的形态，默认停在原来那一套上，
    /// 老用户升级上来不会一打开就觉得列表变了样。
    /// </remarks>
    public bool UseGrid { get; init; }

    /// <summary>默认设置：行列表。</summary>
    public static ViewModeSettings Default { get; } = new();

    /// <summary>
    /// 把越界的值收进合法范围。
    /// </summary>
    /// <remarks>
    /// 目前只有一个布尔项，没有可越界的取值。方法保留是为了与其它设置同一个形状 ——
    /// 以后加「每行几格」之类的项时，不必再去改 <c>JsonViewModeSettingsStore</c> 的调用点。
    /// </remarks>
    public ViewModeSettings Normalized() => this;
}
