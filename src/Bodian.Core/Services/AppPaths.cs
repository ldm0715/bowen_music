namespace Bodian.Core.Services;

/// <summary>
/// 本机数据目录。
/// </summary>
/// <remarks>
/// <b>这些路径是与 P0 探针共用的，不要改。</b> 探针已经用 <c>%LOCALAPPDATA%\Bodian\devid.txt</c>
/// 生成过设备标识、用 <c>session.dat</c> 存过会话；改路径会让客户端换一个新设备标识，
/// 那是账号风控的异常信号。
/// </remarks>
public static class AppPaths
{
    /// <summary><c>%LOCALAPPDATA%\Bodian</c></summary>
    public static string LocalAppData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Bodian");

    /// <summary>设备标识文件。<b>必须与探针共用同一个文件。</b></summary>
    public static string DeviceIdFile => Path.Combine(LocalAppData, "devid.txt");

    /// <summary>会话凭据文件（DPAPI 加密）。<b>必须与探针共用同一个文件。</b></summary>
    public static string CredentialFile => Path.Combine(LocalAppData, "session.dat");

    /// <summary>
    /// 「最近播放」的本地记录（明文 JSON）。
    /// </summary>
    /// <remarks>
    /// <b>与官方客户端的历史不互通</b>：官方存在自己的 SQLite（<c>songDB.db</c>）里，
    /// 本项目不读别人的库。所以这份历史从本客户端第一次播放开始积累。
    /// </remarks>
    public static string PlayHistoryFile => Path.Combine(LocalAppData, "history.json");

    /// <summary>搜索关键词历史（明文 JSON）。</summary>
    public static string SearchHistoryFile => Path.Combine(LocalAppData, "search-history.json");

    /// <summary>
    /// 显示方式偏好（明文 JSON，目前只有「用封面卡片还是行列表」一项）。
    /// </summary>
    /// <remarks>
    /// <b>全局一份</b>：搜索结果的歌单 / 专辑 / 歌手三个页签与「收藏的专辑」「收藏的歌单」共用它，
    /// 所以文件名不带 search —— 它管的是「这个人习惯怎么看封面」，不是某一页的设置。
    /// <b>与 <see cref="SearchHistoryFile"/> 刻意分开</b>：那份是「搜过什么」，这份是「东西怎么排」，
    /// 一个是内容、一个是界面偏好，合在一起以后想同步偏好就得先摘出去。
    /// 也与 <see cref="SettingsFile"/> 分开 —— 那是外观（主题）。
    /// </remarks>
    public static string ViewModeFile => Path.Combine(LocalAppData, "view-mode.json");

    /// <summary>
    /// 界面设置（明文 JSON，目前只有外观一项）。
    /// </summary>
    /// <remarks>
    /// <b>与探针无关</b>：探针是控制台程序，没有界面，不读写这个文件。
    /// 它与 <c>devid.txt</c> / <c>session.dat</c> 不同，不受「必须与探针共用」那条约束。
    /// </remarks>
    public static string SettingsFile => Path.Combine(LocalAppData, "settings.json");

    /// <summary>
    /// 播放偏好（明文 JSON，目前只有播放模式一项）。
    /// </summary>
    /// <remarks>
    /// <b>刻意与 <see cref="SettingsFile"/> 分开、也与音质偏好分开</b>：
    /// 音质已经单独落在 <c>audio-quality.json</c>，两个独立的小偏好挤进一个文件只会让读写互相牵制。
    /// </remarks>
    public static string PlaybackSettingsFile => Path.Combine(LocalAppData, "playback.json");

    /// <summary>
    /// 上次关闭时的窗口位置与大小（明文 JSON）。
    /// </summary>
    /// <remarks>
    /// <b>刻意与 <see cref="SettingsFile"/> 分开</b>：窗口几何是「这台机器」的事，
    /// 而外观偏好是「这个人」的事。混在一个文件里，以后想同步外观就得先把它摘出去。
    /// </remarks>
    public static string WindowFile => Path.Combine(LocalAppData, "window.json");

    /// <summary>
    /// 桌面歌词条的外观偏好（明文 JSON）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="WindowFile"/> 一样刻意单独一份：字号颜色是「这个人」的事，
    /// 位置尺寸是「这台机器」的事，混在一起以后想同步偏好就得先把它摘出去。
    /// </remarks>
    public static string DesktopLyricsSettingsFile => Path.Combine(LocalAppData, "desktop-lyrics.json");

    /// <summary>
    /// 歌词页（全屏歌词）的显示偏好（明文 JSON）。
    /// </summary>
    /// <remarks>
    /// <b>与 <see cref="DesktopLyricsSettingsFile"/> 刻意分开</b>：那份是悬浮歌词条的外观，
    /// 这份是歌词页显示什么。两张界面各有各的偏好，合在一起会让两处的读写互相牵制。
    /// </remarks>
    public static string LyricsSettingsFile => Path.Combine(LocalAppData, "lyrics.json");

    /// <summary>
    /// 桌面歌词条的位置与宽度（明文 JSON）。
    /// </summary>
    /// <remarks>
    /// 复用 <c>JsonWindowPlacementStore</c> 的类型、只换路径，所以文件名要与主窗口的
    /// <see cref="WindowFile"/> 区分开，否则两份记录会互相覆盖。
    /// </remarks>
    public static string DesktopLyricsWindowFile => Path.Combine(LocalAppData, "desktop-lyrics-window.json");

    /// <summary>
    /// 小窗（迷你播放器）的位置（明文 JSON）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同样复用 <c>JsonWindowPlacementStore</c> 的类型、只换路径。三份窗口几何各占一个文件：
    /// 主窗口、桌面歌词条、小窗，任何一个与别人共用都会互相覆盖。
    /// </para>
    /// <para>
    /// <b>只记位置，不记「贴边收起」</b>：收起是临时让路的状态，重启后小窗应该看得见，
    /// 而不是缩在屏幕边上让人以为丢了。
    /// </para>
    /// </remarks>
    public static string MiniPlayerWindowFile => Path.Combine(LocalAppData, "mini-player-window.json");

    /// <summary>日志目录。</summary>
    public static string LogDirectory => Path.Combine(LocalAppData, "logs");
}
