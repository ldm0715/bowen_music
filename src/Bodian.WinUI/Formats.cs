using System.Globalization;
using Bodian.WinUI.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI;

/// <summary>
/// XAML 里用到的显示格式化。<c>x:Bind</c> 支持函数绑定，所以不必为每种格式写一个转换器。
/// </summary>
public static class Formats
{
    /// <summary>秒数版本的时长格式化，供绑定到 <c>double</c> 属性的场合使用。</summary>
    public static string Seconds(double seconds) => Duration(TimeSpan.FromSeconds(seconds));

    /// <summary>百分数。音量这类 0–100 的值用它显示。</summary>
    public static string Percent(double value) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(value):0}%");

    /// <summary>
    /// 播放条上曲名的最大宽度：信息块总宽减去右侧会占位的徽标。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 曲名与徽标放在同一个横向 <see cref="StackPanel"/> 里，徽标才会<b>紧跟曲名</b> ——
    /// 放在 <c>*</c>/<c>Auto</c> 两列的 <c>Grid</c> 里时，<c>*</c> 那一列会吃掉所有剩余宽度，
    /// 徽标就永远钉在最右边界，曲名一短它就飘开。
    /// </para>
    /// <para>
    /// 代价是横向 <c>StackPanel</c> 不约束子元素宽度，长曲名会把徽标顶出信息块 ——
    /// 所以曲名必须自己带 <c>MaxWidth</c>，而它取决于当前显示了几个徽标。
    /// </para>
    /// <para>
    /// <b>徽标宽度是按字号 11 + 左右内边距 6 估的</b>，改徽标样式时要一起改，否则又会被顶出去。
    /// </para>
    /// </remarks>
    public static double PlayerTitleMaxWidth(double infoBlockWidth, bool hasAuditionBadge, bool hasPayBadge)
    {
        // 徽标宽度是按「字号 11 + 左右内边距 6」估的（见 PlayerBar.xaml 里那两个 Border），
        // 改徽标样式时要一起改，否则又会被顶出去。
        const double badge = 36;

        var used = hasAuditionBadge || hasPayBadge ? PlayerBadgeSpacing : 0;

        if (hasAuditionBadge)
        {
            used += badge;
        }

        if (hasPayBadge)
        {
            used += hasAuditionBadge ? badge + PlayerBadgeSpacing : badge;
        }

        return Math.Max(60, infoBlockWidth - used);
    }

    /// <summary>播放条信息块的常规宽度。窄窗口会缩短，曲名按当前宽度为徽标留位。</summary>
    public static double PlayerInfoBlockWidth => 220;

    /// <summary>
    /// 播放条上曲名与徽标之间的间距。<c>PlayerBar.xaml</c> 里那个 <c>Spacing</c> 绑的就是它。
    /// </summary>
    public static double PlayerBadgeSpacing => 6;

    /// <summary>时长。一小时以内用 <c>m:ss</c>，超过用 <c>h:mm:ss</c>。</summary>
    public static string Duration(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    /// <summary>播放条音质按钮：尚未获取实际音质时显示入口名称。</summary>
    public static string PlayerQualityLabel(string quality) =>
        string.IsNullOrWhiteSpace(quality) ? "音质" : quality;

    /// <summary>专辑条目右侧的曲目数。0 时留空 —— 「0 首」在列表里只是噪音。</summary>
    public static string AlbumCount(int count) => count > 0 ? $"{count} 首" : "";

    /// <summary>
    /// 曲目列表里专辑名的字符上限。
    /// </summary>
    /// <remarks>
    /// 列宽是 <c>0.7*</c>，专辑名跟着数据无限长会让同一份列表的列宽忽宽忽窄，
    /// 所以按字数截断。12 个字放得下「仙剑奇侠传电视剧原声带」这类长专辑名。
    /// </remarks>
    private const int AlbumNameMaxChars = 12;

    /// <summary>
    /// 曲目列表里的专辑名。超过 12 个字符时截断，以省略号收尾。
    /// </summary>
    /// <remarks>
    /// 数的是 <c>char</c>（UTF-16 码元）而不是字素簇：中文、日文、韩文都在基本平面内，
    /// 一个字符就是一个码元；只有基本平面外的字符（如「𠮷」）占两个。
    /// 所以截断前查一下末尾是不是高位代理，免得切出半个字符。
    /// </remarks>
    public static string AlbumName(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= AlbumNameMaxChars)
        {
            return value ?? "";
        }

        var length = char.IsHighSurrogate(value[AlbumNameMaxChars - 1]) ? AlbumNameMaxChars - 1 : AlbumNameMaxChars;

        return string.Concat(value.AsSpan(0, length), "…");
    }

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

    /// <summary>
    /// 封面地址转图片源。
    /// </summary>
    /// <remarks>
    /// 同样是为了绕开 <c>x:Bind</c> 的限制：<c>Uri</c> 不能直接绑到 <see cref="ImageSource"/>（WMC1121）。
    /// 返回 <c>null</c> 时 Image 是空的，外层 Border 的底色会露出来。
    /// </remarks>
    public static ImageSource? CoverSource(Uri? uri) => CoverImageCache.Get(uri, 256);

    public static Visibility CommentImageVisibility(Uri? uri) => Visible(uri is not null);
    public static Visibility CommentRepliesVisibility(long count) => Visible(count > 0);
    public static string CommentCount(long count) => count.ToString("N0", CultureInfo.CurrentCulture);
    public static string CommentReplies(long count) => $"{CommentCount(count)} 条回复";
    public static string CommentMetadata(string time, string location) => string.IsNullOrWhiteSpace(location)
        ? time : string.IsNullOrWhiteSpace(time) ? $"IP 属地：{location}" : $"{time} · {location}";

    public static Visibility CommentDetailVisibility(bool reply, long replyCount) => Visible(!reply && replyCount > 0);
    public static string CommentReplyHeading(long count) => $"全部回复 · {count:N0}";
    public static Visibility VisibleWhenFalse(bool value) => Visible(!value);

}
