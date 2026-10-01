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

    /// <summary>日志目录。</summary>
    public static string LogDirectory => Path.Combine(LocalAppData, "logs");
}
