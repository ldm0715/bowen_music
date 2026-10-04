using System.Globalization;
using Bodian.Core.Models;
using Bodian.WinUI.Controls;
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
    /// <param name="rowColumnWidth">
    /// 播放条那一行**左栏**的实际宽度（<c>PlayerBar.xaml</c> 里
    /// <c>TransportRow.ColumnDefinitions[0].ActualWidth</c>）。
    /// </param>
    /// <remarks>
    /// <para>
    /// ★ <b>入参不能是信息块自己的宽度。</b> 信息块是内容自适应的
    /// （<c>HorizontalAlignment="Left"</c>，徽标才会紧跟曲名），它的宽度反过来取决于曲名被限到多宽 ——
    /// 拿它当上限就成了循环：首轮测量时是 0，落在下面的下限上之后再涨不回来。
    /// 曾经因此把**每一首歌名**都卡在 4 个字（2026-10-04 发现）。
    /// 左栏宽度由窗口决定、与内容无关，用它才稳定。
    /// </para>
    /// <para>
    /// 徽标宽度按「字号 11 + 左右内边距 6」估（见 <c>PlayerBar.xaml</c> 里那两个 Border）；
    /// <c>PlayerInfoBlockChrome</c> 那几项同理，改 <c>PlayerBar.xaml</c> 时要一起改。
    /// </para>
    /// </remarks>
    public static double PlayerTitleMaxWidth(double rowColumnWidth, bool hasAuditionBadge, bool hasPayBadge)
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

        var infoBlock = Math.Min(PlayerInfoBlockMaxWidth, rowColumnWidth) - PlayerInfoBlockChrome;
        return Math.Max(60, infoBlock - used);
    }

    /// <summary>信息块能占的最大宽度。与 <c>PlayerBar.xaml</c> 里 <c>TrackInfoGroup.MaxWidth</c> 一致。</summary>
    public static double PlayerInfoBlockMaxWidth => 376;

    /// <summary>
    /// 信息块里除曲名之外的固定占位：封面 48 + 两段列间距 12×2 + 收藏/分享各 32 + 间距 4。
    /// </summary>
    /// <remarks>
    /// 这几项都写在 <c>PlayerBar.xaml</c> 里，改那边时要一起改 —— 数值估错的表现是
    /// 曲名把徽标或统计按钮顶出去。
    /// </remarks>
    private const double PlayerInfoBlockChrome = 48 + 12 + 12 + 32 + 32 + 4;

    /// <summary>
    /// 歌词页标题行曲名的最大宽度：标题行可用宽度减去付费徽标与行内间距。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="PlayerTitleMaxWidth"/> 同一个理由 —— 标题与徽标在横向
    /// <see cref="StackPanel"/> 里才会紧贴，代价是 StackPanel 不约束子元素宽度，
    /// 所以曲名必须自己带上限，否则长标题会把徽标顶出屏幕。
    /// <b>徽标宽度按字号 10 + 左右内边距 6 + 边框估</b>，改徽标样式时要一起改。
    /// </remarks>
    public static double LyricsHeadingMaxWidth(double availableWidth, bool hasPayBadge)
    {
        const double badge = 52;
        const double spacing = 8;

        var reserved = hasPayBadge ? badge + spacing : 0;
        return Math.Max(160, availableWidth - reserved);
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

    /// <summary>分类卡角标里的专辑数。同样是 0 留空。</summary>
    public static string AlbumTotal(int count) => count > 0 ? $"{count} 张" : "";

    /// <summary>
    /// 封面右下角那个角标要不要显示。
    /// </summary>
    /// <remarks>
    /// 数为 0 表示这个来源没给这个字段（不是「这张专辑是空的」），
    /// 那时连角标本身都不该出现 —— 只让文字留空的话，封面上会浮一个空的胶囊。
    /// 曲目数与专辑数共用这一条判据，两处角标长得一样、隐现也该一样。
    /// </remarks>
    public static Visibility HasTracks(int count) => Visible(count > 0);

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

    /// <summary>
    /// 歌手页头部那一行：<c>484w8+ 粉丝 · 1708 首歌曲 · 47 张专辑</c>，缺的部分自动省掉。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 三个数都不是必然有的：从曲目行「查看歌手」进来时只知道名字，
    /// 详情还没回来之前一个计数都没有，那时整行不显示（<see cref="ArtistMetaVisibility"/>）。
    /// </para>
    /// <para>
    /// 粉丝数沿用评论角标那套截断（超过一万显示 <c>w</c> 后缀）——
    /// 4848318 直接写出来会把这一行撑得很长，而精确到个位的粉丝数没有意义。
    /// </para>
    /// </remarks>
    public static string ArtistMeta(Artist? artist)
    {
        if (artist is null)
        {
            return "";
        }

        var parts = new List<string>(3);

        if (artist.HasFans)
        {
            parts.Add($"{CommentCountLabel.Format(artist.FansCount)} 粉丝");
        }

        if (artist.SongCount > 0)
        {
            parts.Add($"{artist.SongCount} 首歌曲");
        }

        if (artist.AlbumCount > 0)
        {
            parts.Add($"{artist.AlbumCount} 张专辑");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>一个计数都没有时整行不显示，而不是留一行空白。</summary>
    public static Visibility ArtistMetaVisibility(Artist? artist) =>
        Visible(!string.IsNullOrEmpty(ArtistMeta(artist)));

    // ── 收藏 / 关注按钮的两态（reverse/findings/13）─────────────────────────
    //
    // 状态是 bool?：null 代表「还没判定出来」（未登录或读取失败）。
    // 按未收藏/未关注显示 —— 那是更保守的一侧，此时点下去会走「收藏/关注」分支。

    /// <summary>歌单收藏按钮文案。</summary>
    public static string CollectLabel(bool? collected) => collected == true ? "已收藏" : "收藏";

    /// <summary>
    /// 歌单收藏按钮的图标路径：空心星（未收藏）/ 实心星（已收藏）。
    /// </summary>
    /// <remarks>
    /// 直接返回路径文本而不是「键」，因为 <c>x:Bind</c> 的函数绑定**不支持函数套函数**
    /// （<c>IconPaths(CollectIconKey(x))</c> 会生成缺参数的代码，编译期报 <c>p0 不存在</c>）。
    /// </remarks>
    public static string CollectIcon(bool? collected)
        => IconPaths(collected == true ? "IconStarFilled" : "IconStar");

    /// <summary>歌手关注按钮文案。</summary>
    public static string FollowLabel(bool? followed) => followed == true ? "已关注" : "关注";

    /// <summary>歌手关注按钮的图标路径：加人（未关注）/ 实心加人（已关注）。同上，不套函数。</summary>
    public static string FollowIcon(bool? followed)
        => IconPaths(followed == true ? "IconPersonAddFilled" : "IconPersonAdd");

    /// <summary>
    /// 按资源键取图标的**路径文本**，给 <c>Controls/Icon</c> 的 <c>Data</c> 用。
    /// </summary>
    /// <remarks>
    /// 只有一个场景需要它：**XAML 里写不出静态资源** —— 图标由 ViewModel 按状态选
    /// （收藏两态、关注两态、主题三态、展开箭头、曲目菜单）。于是 ViewModel 只给出一个符号键，
    /// 这里再把键换成路径文本。
    /// <para>
    /// <b>ViewModel 只存键、不存路径</b>：路径是几 KB 的几何数据，塞进 ViewModel 会跟着数据流
    /// 到处走；键是一行字符串，还能被离线测试断言。
    /// </para>
    /// <para>
    /// 图标几何与主题无关、颜色一律走 Foreground 继承，所以这里查
    /// <see cref="Application.Current"/> 不触犯 ui-refresh.md §7（那条禁的是**跟随主题的颜色**）。
    /// </para>
    /// </remarks>
    public static string IconPaths(string key) => IconGeometry.Paths(key);
}
