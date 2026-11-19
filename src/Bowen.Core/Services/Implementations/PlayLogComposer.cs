using System.Globalization;
using System.Text;
using Bowen.Core.Models;

namespace Bowen.Core.Services.Implementations;

/// <summary>
/// 拼装 <c>log.kuwo.cn/music.yl</c> 的播放日志，并做 XOR + Base64 编码。
/// </summary>
/// <remarks>
/// <para>
/// 格式来自 2026-11-18 对官方 Android 5.9.8 的实抓（详见 <c>docs/play-stats.md</c> §9）。
/// 字段顺序、键名（含 <c>STARTPlAY_MODE</c> 的拼写）都按官方原样保留。
/// </para>
/// <para>
/// <b>客户端标识沿用移动端的值。</b> 官方桌面端的 <c>music.yl</c> 没有被统计采纳，
/// 移动端格式的探针实测会计入，所以这里按移动端模板发送。
/// </para>
/// </remarks>
internal static class PlayLogComposer
{
    private const string ClientSource = "bodplayer_ar_5.9.8";
    private const string From = "bodplayer_ar_5.9.8_oppo";

    private static readonly byte[] Key = Encoding.UTF8.GetBytes("Z9o+Lw@H");

    /// <summary>拼出明文日志。不含编码，便于单测逐字段断言。</summary>
    internal static string Build(
        Track track,
        TimeSpan played,
        string uid,
        string deviceId,
        DateTimeOffset now)
    {
        var duration = Math.Max(1, (int)Math.Round(track.Duration.TotalSeconds));
        var playedSeconds = Math.Clamp((int)Math.Round(played.TotalSeconds), 0, duration);
        var time = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        return new StringBuilder(1024)
            .Append("2%09<SRC:").Append(ClientSource)
            .Append("|ACT:BOD_PLAY_MUSIC|T:1|RID:").Append(track.Id)
            .Append("|VID:|DUR:").Append(duration)
            .Append("|NA:").Append(track.Title)
            .Append("|AR:").Append(track.ArtistText)
            .Append("|PT:").Append(playedSeconds)
            .Append("|AQ:3|PLAY_TIME:").Append(playedSeconds)
            .Append("|PLAYPAGE_TIME:").Append(playedSeconds)
            .Append("|APP_TIME:").Append(playedSeconds)
            .Append("|LSRC:我的->音乐->上一次播放列表|PSRC:playing_more->专辑详情页")
            .Append("|MV:0|PATH:|SRC:|FG:1|LOCK:2|DELAY:0|BLKCNT:0|DELAY_FIX:0")
            .Append("|FMT:flac|SCENE:0|PTYPE:0|FTYPE:1|PLAY_STATE:0|PLAY_MODE:0")
            .Append("|RECOMMEND_MODE:-1|PLAYER_THEME:poster|ERROR_CODE:0|LOW_DEV:0")
            .Append("|free_mode:0|SPT:").Append(duration)
            .Append("|STARTPlAY_MODE:1|MUSIC_EFFECT:清澈人声|TID:|cut_song_type:2")
            .Append("|BR:2000|CACHE:0|SPEED:0|LAST_SPEED:0.0|ERROR_DESC:success")
            .Append("|HTTPHOST:127.0.0.1|HTTPCODE:200|RESPONSE_CODE:200|HTTPURL:")
            .Append("|PROXY_TYPE:NONE_PROXY|HEADSET_CONNECT:0")
            .Append("|PROD:bodplayer|FROM:").Append(From)
            .Append("|CHANNEL:oppo|VER:5.9.8|VER_CODE:598|BUILD_NO:485|PLAT:ar")
            .Append("|DEV:OnePlus PKG110|OSV:16|CT:").Append(time)
            .Append("|CIP:|U:").Append(deviceId)
            .Append("|UUID:").Append(deviceId)
            .Append("|Q36:").Append(deviceId)
            .Append("|ANDROID_ID:").Append(deviceId)
            .Append("|OAID:").Append(deviceId)
            .Append("|UI:").Append(uid)
            .Append("|NE_TYPE:WIFI|IS_VIP:0>")
            .ToString();
    }

    /// <summary>XOR 后 Base64，对应官方 <c>encv=1</c> 的 body。</summary>
    internal static string Encode(string log)
    {
        var bytes = Encoding.UTF8.GetBytes(log);

        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] ^= Key[i % Key.Length];
        }

        return Convert.ToBase64String(bytes);
    }
}
