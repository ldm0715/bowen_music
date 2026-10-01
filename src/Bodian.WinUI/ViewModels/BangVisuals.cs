using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 排行榜相关的显示辅助，供 <c>x:Bind</c> 的函数绑定用。
/// </summary>
/// <remarks>
/// <b>函数绑定不接受 <c>Converter</c></b>（会报 WMC1121），所以这类小转换
/// 只能写成静态方法直接返回目标类型 —— 与 <c>Formats</c> 那条规矩一样。
/// </remarks>
public static class BangVisuals
{
    /// <summary>
    /// 名次的颜色：前三名强调，其余用次要文字色。
    /// </summary>
    /// <remarks>
    /// <b>颜色从主题里取，不硬编码。</b> 深浅色主题下 <c>TextFillColorSecondary</c>
    /// 会自动换，写死一个灰会在另一种主题下看不清 ——
    /// 这与 P5 歌词踩过的那个坑是同一类（当时读主题画刷读出了白字白底，
    /// 那是因为取错了画刷；这里取的是标准的那几个）。
    /// </remarks>
    public static Brush RankBrush(int rank)
    {
        var key = rank <= 3 ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush";

        return Application.Current.Resources[key] as Brush
               ?? new SolidColorBrush(Colors.Gray);
    }
}
