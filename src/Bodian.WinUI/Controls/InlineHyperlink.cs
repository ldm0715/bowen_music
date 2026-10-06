using Microsoft.UI.Xaml.Documents;
using Windows.Foundation;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 行内链接的构造。播放条第二行与曲目行里的歌手 / 专辑共用这一份。
/// </summary>
/// <remarks>
/// <para>
/// <b>不设 Foreground</b>：颜色由 <c>Themes/Theme.xaml</c> 里覆写的
/// <c>HyperlinkForeground</c> / <c>HyperlinkForegroundPointerOver</c> / <c>HyperlinkForegroundPressed</c>
/// 三个键决定（静止灰、悬停强调色）。在元素上写局部值会压掉模板的悬停态 —— 这正是内联
/// <see cref="Hyperlink"/> 与 <c>HyperlinkButton</c> 的关键差别：后者走的是自己那一套
/// <c>HyperlinkButtonForeground*</c> 键，两套色值并不相同。要两边长得一样，就必须都是内联链接。
/// </para>
/// <para>
/// <b><see cref="UnderlineStyle.None"/> 显式关掉</b>：默认是 <c>Single</c>（静止就带下划线），
/// 而这两处要的都是和普通文字同款的干净灰字。
/// </para>
/// </remarks>
internal static class InlineHyperlink
{
    public static Hyperlink Create(string text, TypedEventHandler<Hyperlink, HyperlinkClickEventArgs> onClick)
    {
        var link = new Hyperlink { UnderlineStyle = UnderlineStyle.None };
        link.Inlines.Add(new Run { Text = text });
        link.Click += onClick;
        return link;
    }
}
