namespace Bodian.Core.Models.Home;

/// <summary>
/// 发现页布局里的一个模块（还没取内容）。
/// </summary>
/// <remarks>
/// <para>
/// <c>service/home/index</c> **只给布局**：一串 <see cref="Id"/> + <see cref="Type"/> + 名字，
/// 每个模块的内容要再按 <c>service/home/module?moduleId=N</c> 单独拉。
/// 实测这 12 个模块的 name 与 type 是对得上的（见 <c>fixtures/home-index-raw.json</c>）。
/// </para>
/// <para>
/// <b>内容的形状由 <see cref="Type"/> 决定，不能只看 JSON 的键名</b>：
/// <c>songList</c> 这个键在 type 4/11 里是「曲目分组」，在 type 5 里是「歌单卡片」。
/// 所以取内容时要按 <see cref="Type"/> 挑解析类型。
/// </para>
/// </remarks>
/// <param name="Id">模块 id，取内容时用它拼 <c>moduleId</c>。</param>
/// <param name="Type">模块类型。<b>决定内容怎么解析</b>，见 <c>HomeModuleContent</c>。</param>
/// <param name="Name">模块名。界面上当分组标题用。</param>
/// <param name="DelayMs">服务端建议的延迟展示毫秒数（实测多为 0，个别是 2000）。</param>
public sealed record HomeModule(int Id, int Type, string Name, int DelayMs)
{
    /// <summary>这个类型的内容本项目支持渲染。</summary>
    /// <remarks>
    /// <para>
    /// 支持的是实测拿到过形状的五种：曲目数组（3 / 10）、曲目分组（4 / 11）、歌单卡片（5）。
    /// </para>
    /// <para>
    /// <b>排行榜（2）有意不在其中</b>：它已经是侧栏的独立一项（数据源是
    /// <c>service/home/bangNew</c>，那个端点给的榜更全、还带分组），
    /// 放在发现页里是重复的入口。
    /// </para>
    /// <para>
    /// 其余类型（轮播图、广告、实验室、音乐日历、数字专辑馆、听点不一样的视频）
    /// **内容形状复杂且不是内容流**，本版不渲染。
    /// </para>
    /// </remarks>
    public bool IsSupported => Type is 3 or 4 or 5 or 10 or 11;
}
