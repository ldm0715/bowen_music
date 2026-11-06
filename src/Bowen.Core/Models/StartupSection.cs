using System.Text.Json.Serialization;
using Bowen.Core.Services;

namespace Bowen.Core.Models;

/// <summary>
/// 启动后进入的侧栏根页。
/// </summary>
/// <remarks>
/// <b>落盘时必须写成字符串</b>（见 <c>ThemeSettingsJsonContext</c>）：枚举的数值顺序
/// 随时可能重排，写成数字会让旧设置文件静默读成别的页面。
/// <para>
/// 账号专属项（<see cref="Favorites"/> / <see cref="CollectedAlbums"/> /
/// <see cref="CollectedPlaylists"/> / <see cref="FollowedArtists"/>）在未登录启动时
/// 回退到 <see cref="Discover"/>，见 <c>MainWindow.NavigateToStartupPage</c>。
/// </para>
/// </remarks>
[JsonConverter(typeof(TolerantEnumConverter<StartupSection>))]
public enum StartupSection
{
    /// <summary>发现页。匿名也能用。</summary>
    Discover,

    /// <summary>排行榜。匿名也能用。</summary>
    Bangs,

    /// <summary>乐库。匿名也能用。</summary>
    Library,

    /// <summary>我喜欢的。默认值，保持此前「启动进我喜欢的」的行为。</summary>
    Favorites,

    /// <summary>最近播放。读本地记录，匿名也能用。</summary>
    Recent,

    /// <summary>收藏的专辑。需要登录。</summary>
    CollectedAlbums,

    /// <summary>收藏的歌单。需要登录。</summary>
    CollectedPlaylists,

    /// <summary>关注的歌手。需要登录。</summary>
    FollowedArtists,
}
