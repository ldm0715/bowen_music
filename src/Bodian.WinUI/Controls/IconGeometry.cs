using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 把 <c>Themes/Icons.xaml</c> 里的路径文本转成 <see cref="Geometry"/>。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>每次调用都返回一个新实例，这是这个类存在的全部理由。</b>
/// WinUI 不允许把一个 <see cref="Geometry"/> 资源用 <c>{StaticResource}</c> 直接赋给
/// Geometry 属性 —— 连内置的 <c>PathIcon.Data</c> 都会在**启动时**抛
/// <c>XamlParseException: Failed to assign to property</c>，整个应用打不开
/// （编译期毫无征兆）。所以资源字典里存的是路径**字符串**，由这里逐个转成
/// 归属自己的几何。
/// </para>
/// <para>
/// 转换走 XAML 自己的类型转换器：路径简写（SVG path data）本来就是它的输入格式，
/// 与 <c>&lt;Geometry&gt;M ...&lt;/Geometry&gt;</c> 是同一套解析。颜色不在这里管 ——
/// 图标的颜色一律由 <c>Foreground</c> 继承，深浅主题和高对比度自动跟随。
/// </para>
/// </remarks>
internal static class IconGeometry
{
    /// <summary>
    /// 按资源键取路径文本；键不存在时返回空串（图标画成空白）。
    /// </summary>
    /// <remarks>
    /// 给「XAML 里写不出 <c>{StaticResource}</c>」的场合用 —— 图标由 ViewModel 按状态选。
    /// 见 <see cref="Bodian.WinUI.Formats.IconPaths"/>。
    /// </remarks>
    internal static string Paths(string? key)
        => !string.IsNullOrEmpty(key)
            && Application.Current is { } app
            && app.Resources.TryGetValue(key, out var value)
            && value is string pathData
                ? pathData
                : "";

    /// <summary>
    /// 把路径文本转成几何；文本为空或转换失败时返回 <c>null</c>（那颗图标画成空白）。
    /// </summary>
    /// <remarks>
    /// 两条路：先走 XAML 的类型转换器（快，通常就够）；拿不到再退回 <see cref="XamlReader"/> 直接
    /// 解析一次 —— 后者就是资源字典里 <c>&lt;Geometry&gt;M ...&lt;/Geometry&gt;</c> 当年走过的那条路，
    /// 确定能吃路径简写，作为兜底不会白屏。
    /// </remarks>
    internal static Geometry? From(string? pathData)
    {
        if (string.IsNullOrWhiteSpace(pathData))
        {
            return null;
        }

        try
        {
            if (XamlBindingHelper.ConvertValue(typeof(Geometry), pathData) is Geometry converted)
            {
                return converted;
            }
        }
        catch (Exception)
        {
            // 落到下面的兜底；两条都不行时返回 null，不让它炸主窗口。
        }

        try
        {
            var xaml = $"<Geometry xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">{pathData}</Geometry>";
            return XamlReader.Load(xaml) as Geometry;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
