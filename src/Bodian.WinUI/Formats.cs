using System.Globalization;
using Bodian.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Bodian.WinUI;

/// <summary>
/// XAML 里用到的显示格式化。<c>x:Bind</c> 支持函数绑定，所以不必为每种格式写一个转换器。
/// </summary>
public static class Formats
{
    /// <summary>秒数版本的时长格式化，供绑定到 <c>double</c> 属性的场合使用。</summary>
    public static string Seconds(double seconds) => Duration(TimeSpan.FromSeconds(seconds));

    /// <summary>时长。一小时以内用 <c>m:ss</c>，超过用 <c>h:mm:ss</c>。</summary>
    public static string Duration(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// 列表右侧的可用音质。**显示的是最高可播档位**，实际拿到什么以播放条上的标注为准
    /// （服务端可能降级）。
    /// </summary>
    public static string Quality(IReadOnlyList<AudioQuality> qualities) =>
        qualities.Count == 0 ? "" : Describe(qualities[0]);

    /// <summary>
    /// 付费标识文案。
    /// </summary>
    /// <remarks>
    /// <b>只是服务端标记的展示</b>，不代表当前账号一定播不了 —— 权限永远由 checkRight 裁决。
    /// </remarks>
    public static string PayLabel(bool requiresVip, bool requiresPurchase) =>
        requiresVip ? "VIP" : requiresPurchase ? "付费" : "";

    /// <summary>
    /// 付费标识该不该显示。
    /// </summary>
    /// <remarks>
    /// 直接返回 <see cref="Visibility"/> 而不是 <c>bool</c>：
    /// <b><c>x:Bind</c> 的函数绑定不接受 <c>Converter</c></b>（只有属性绑定接受），
    /// 返回 bool 会报 WMC1121「return type Boolean must match binding target type Visibility」。
    /// </remarks>
    public static Visibility PayLabelVisibility(bool requiresVip, bool requiresPurchase) =>
        requiresVip || requiresPurchase ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// <c>bool</c> 转 <see cref="Visibility"/>。
    /// </summary>
    /// <remarks>
    /// 有了它就不必为每个布尔可见性都在 XAML 里挂转换器资源 ——
    /// 而且函数绑定不接受 <c>Converter</c>，转换器写法在这里根本用不了。
    /// </remarks>
    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 播放条第二行：<c>歌手 · 专辑</c>。
    /// </summary>
    /// <remarks>
    /// 拼成一个字符串而不是两个 <c>TextBlock</c> 加一个分隔符：横向 <c>StackPanel</c> 不会约束子元素宽度，
    /// 分了家的两个文本都没法省略号截断，长专辑名会把整条播放条撑开。一个字符串配
    /// <c>TextTrimming</c> 才有得截。
    /// </remarks>
    public static string ArtistLine(string artist, string album)
    {
        if (string.IsNullOrWhiteSpace(album))
        {
            return artist;
        }

        return string.IsNullOrWhiteSpace(artist) ? album : $"{artist} · {album}";
    }

    /// <summary>歌词行的透明度：当前行实心，其余压暗。</summary>
    public static double LyricLineOpacity(bool isCurrent) => isCurrent ? 1.0 : 0.45;

    /// <summary>歌词行的字号：当前行放大。</summary>
    public static double LyricLineSize(bool isCurrent) => isCurrent ? 20 : 16;

    /// <summary>
    /// 封面地址转图片源。
    /// </summary>
    /// <remarks>
    /// 同样是为了绕开 <c>x:Bind</c> 的限制：<c>Uri</c> 不能直接绑到 <see cref="ImageSource"/>（WMC1121）。
    /// 返回 <c>null</c> 时 Image 是空的，外层 Border 的底色会露出来。
    /// </remarks>
    public static ImageSource? CoverSource(Uri? uri) => uri is null ? null : new BitmapImage(uri);

    private static string Describe(AudioQuality quality) => quality switch
    {
        AudioQuality.Lossless => "无损",
        AudioQuality.High => "高",
        AudioQuality.Standard => "标准",
        _ => quality.ToString(),
    };
}
