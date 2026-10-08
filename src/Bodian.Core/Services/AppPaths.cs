using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Services;

/// <summary>
/// 本机数据目录。路径按「什么级别的数据」分三类，见 <see cref="AccountDirectory"/>。
/// </summary>
/// <remarks>
/// <para>
/// <b>这些路径是与 P0 探针共用的</b>（<c>tools/Bodian.Probe</c> 有自己的同名副本
/// <c>ProbePaths</c>）。探针已经用 <c>devid.txt</c> 生成过设备标识、用 <c>session.dat</c>
/// 存过会话；两边不同步会让它们各写各的，共用设备标识那个前提就没了。
/// </para>
/// <para>
/// <b>不要改目录名。</b> 目录里那个 <c>devid.txt</c> 是设备标识，换路径就等于换一台设备，
/// 服务端看到的是「同一个账号突然从不认识的机器登录」—— 那是账号风控的异常信号，
/// 而且会话、队列、历史、偏好会一起丢。
/// </para>
/// <para>
/// <b>三类作用域</b>，别一刀切：<i>设备级</i>（<c>devid.txt</c>、<c>logs\</c>、<c>cache\covers\</c>）
/// 跨账号共用是<b>要求</b>；<i>个人级</i>（窗口几何、主题、快捷键、音质、播放模式、音量…）
/// 共用<b>合理</b>，「这个人怎么用这个软件」不该换号就重设；<i>账号级</i>
/// （见 <see cref="AccountsDirectory"/>）才是该按账号分的。设计依据见 <c>docs/multi-account.md</c>。
/// </para>
/// <para>
/// 2026-10-08 曾从 <c>Bodian</c> 改名为 <c>Bowen</c>，当时带过一次性的目录迁移；
/// 迁移执行完即已删除（见 <c>docs/roadmap.md</c> 的命名更正一节）。
/// **从改名前的版本升级上来的用户会拿到一个新的设备标识。**
/// </para>
/// </remarks>
public static class AppPaths
{
    /// <summary><c>%LOCALAPPDATA%\Bowen</c></summary>
    public static string LocalAppData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Bowen");

    /// <summary>设备标识文件。<b>必须与探针共用同一个文件。</b></summary>
    public static string DeviceIdFile => Path.Combine(LocalAppData, "devid.txt");

    /// <summary>会话凭据文件（DPAPI 加密）。<b>必须与探针共用同一个文件。</b></summary>
    public static string CredentialFile => Path.Combine(LocalAppData, "session.dat");

    /// <summary>
    /// 记住的账号清单（DPAPI 加密）：多份凭据 + 各自上次使用的时刻，供账号快捷切换用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>与 <see cref="CredentialFile"/> 分开，且后者的格式一行不改。</b> <c>session.dat</c> 只有一份、
    /// 与 P0 探针共用、JSON 字段名是对外契约 —— 往里塞数组会把这三条一起破坏。
    /// 两者的关系是「当前是谁」与「记住过谁」。
    /// </para>
    /// <para>
    /// <b>名字刻意不叫 <c>accounts.dat</c> 也不叫 <c>sessions.dat</c></b>：前者与
    /// <see cref="AccountsDirectory"/> 那个目录极易混为一谈，后者与 <c>session.dat</c> 只差一个字母。
    /// 加密用的 entropy 也与 session 那份<b>不同</b>，见 <c>DpapiRememberedAccountsStore</c>。
    /// </para>
    /// </remarks>
    public static string RememberedAccountsFile => Path.Combine(LocalAppData, "remembered-accounts.dat");

    /// <summary>未登录时数据落的桶名。见 <see cref="AccountDirectory(string)"/>。</summary>
    public const string AnonymousScope = "anonymous";

    /// <summary><c>accounts</c> 这一层的目录名。需要一个名字而不是完整路径的地方（测试注入根目录）用它。</summary>
    public const string AccountsDirectoryName = "accounts";

    /// <summary>
    /// 账号级数据的根目录：<c>%LOCALAPPDATA%\Bowen\accounts</c>。
    /// </summary>
    /// <remarks>
    /// <b>只有「换账号就该跟着换」的三项进这里</b>：播放队列、最近播放、搜索历史。
    /// 设备级的（<c>devid.txt</c>、<c>logs\</c>、<c>cache\covers\</c>）与个人级的
    /// （窗口几何、主题、快捷键、音质、播放模式…）都留在 <see cref="LocalAppData"/> 根下 ——
    /// 尤其 <c>devid.txt</c> 绝不能按账号分，那会变成 N 个设备标识，是账号风控的异常信号。
    /// </remarks>
    public static string AccountsDirectory => Path.Combine(LocalAppData, AccountsDirectoryName);

    /// <summary>播放队列快照的文件名。见 <see cref="AccountFilePath"/>。</summary>
    public const string PlayQueueFileName = "queue.json";

    /// <summary>
    /// 「最近播放」的文件名。
    /// </summary>
    /// <remarks>
    /// <b>与官方客户端的历史不互通</b>：官方存在自己的 SQLite（<c>songDB.db</c>）里，
    /// 本项目不读别人的库。所以这份历史从本客户端第一次播放开始积累。
    /// </remarks>
    public const string PlayHistoryFileName = "history.json";

    /// <summary>搜索关键词历史的文件名。</summary>
    public const string SearchHistoryFileName = "search-history.json";

    /// <summary>
    /// 某个作用域的数据目录：<c>accounts\&lt;scope&gt;</c>。
    /// </summary>
    /// <param name="scope">
    /// <see cref="AnonymousScope"/>，或某个账号的 uid。见 <see cref="ICurrentAccount.Scope"/>。
    /// </param>
    /// <remarks>
    /// <b>scope 会先校验再拼路径。</b> uid 来自服务端，正常是纯数字，但把外部字符串直接拼进路径
    /// 等于把目录逃逸的口子开着（<c>..</c>、分隔符、非法字符）。校验不过的一律回落到
    /// <see cref="AnonymousScope"/>：失败方向是「写进了匿名桶」，比「写到数据目录外面」安全得多。
    /// 这里不记日志 —— 本类是纯路径助手，没有日志设施；异常 uid 属于理论上才有的情况。
    /// </remarks>
    public static string AccountDirectory(string scope) => AccountDirectory(scope, LocalAppData);

    /// <summary>
    /// 同上，但根目录可指定。<b>测试指向临时目录</b>，免得「清理播放记录」这类用例动到本机真实数据。
    /// </summary>
    public static string AccountDirectory(string scope, string rootDirectory) =>
        Path.Combine(rootDirectory, AccountsDirectoryName, IsSafeScope(scope) ? scope : AnonymousScope);

    /// <summary>某个作用域下的某个文件，例如 <c>accounts\&lt;uid&gt;\queue.json</c>。</summary>
    public static string AccountFilePath(string scope, string fileName) =>
        Path.Combine(AccountDirectory(scope), fileName);

    /// <inheritdoc cref="AccountFilePath(string, string)" />
    public static string AccountFilePath(string scope, string fileName, string rootDirectory) =>
        Path.Combine(AccountDirectory(scope, rootDirectory), fileName);

    private static bool IsSafeScope(string? scope)
    {
        if (string.IsNullOrEmpty(scope) || scope is "." or "..")
        {
            return false;
        }

        // 只认「不含路径分隔符与非法字符」的单一名字。用 GetInvalidFileNameChars 而不是
        // 自己列字符集：不同平台（探针可能在别处跑）的集合不一样，交给运行时判断。
        return scope.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && scope.IndexOf(Path.DirectorySeparatorChar) < 0
            && scope.IndexOf(Path.AltDirectorySeparatorChar) < 0;
    }

    /// <summary>
    /// 显示方式偏好（明文 JSON，目前只有「用封面卡片还是行列表」一项）。
    /// </summary>
    /// <remarks>
    /// <b>全局一份</b>：搜索结果的歌单 / 专辑 / 歌手三个页签与「收藏的专辑」「收藏的歌单」共用它，
    /// 所以文件名不带 search —— 它管的是「这个人习惯怎么看封面」，不是某一页的设置。
    /// <b>与 <see cref="SearchHistoryFileName"/> 刻意分开</b>：那份是「搜过什么」，这份是「东西怎么排」，
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

    /// <summary>输入法兼容偏好，默认关闭；在 XAML 初始化前读取。</summary>
    public static string InputMethodSettingsFile => Path.Combine(LocalAppData, "input-method.json");

    /// <summary>
    /// 可再生的数据目录（缓存）。**删掉不影响正确性**，只会让下次慢一点。
    /// </summary>
    /// <remarks>
    /// 单独一层，以后加音频缓存之类不必再改这里。日志目录不放在它下面 ——
    /// 日志是排查用的，不该被「清除缓存」一起清掉。
    /// </remarks>
    public static string CacheDirectory => Path.Combine(LocalAppData, "cache");

    /// <summary>封面图片的磁盘缓存。见 <c>docs/settings.md</c> 的封面缓存一节。</summary>
    public static string CoverCacheDirectory => Path.Combine(CacheDirectory, "covers");

    /// <summary>
    /// 应用内快捷键的键位绑定（明文 JSON）。
    /// </summary>
    /// <remarks>
    /// <b>与界面偏好分开存</b>：键位是「这个人怎么用键盘」，改起来频率低、
    /// 而且坏了会让整套快捷键失灵，单独一份便于出问题时直接删掉回到默认。
    /// </remarks>
    public static string ShortcutFile => Path.Combine(LocalAppData, "shortcuts.json");

    /// <summary>
    /// 播放偏好（明文 JSON，目前只有播放模式一项）。
    /// </summary>
    /// <remarks>
    /// <b>刻意与 <see cref="SettingsFile"/> 分开、也与音质偏好分开</b>：
    /// 音质已经单独落在 <c>audio-quality.json</c>，两个独立的小偏好挤进一个文件只会让读写互相牵制。
    /// </remarks>
    public static string PlaybackSettingsFile => Path.Combine(LocalAppData, "playback.json");

    /// <summary>
    /// 播放音量偏好（明文 JSON）。
    /// </summary>
    /// <remarks>
    /// <b>不含静音</b>：静音是会话级标记，落盘会造出「启动后没声音、用户以为坏了」这一后果。
    /// <b>单独一份</b>的理由与队列文件相同 —— 音量是拖动时的高频写，与播放模式、队列混在一起
    /// 只会让读写互相牵制。
    /// </remarks>
    public static string VolumeFile => Path.Combine(LocalAppData, "volume.json");

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
