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
    /// <summary>把路径文本转成几何；文本为空或转换失败时返回 <c>null</c>（图标画成空白）。</summary>
    internal static Geometry? From(string? pathData)
    {
        if (string.IsNullOrWhiteSpace(pathData))
        {
            return null;
        }

        try
        {
            return XamlBindingHelper.ConvertValue(typeof(Geometry), pathData) as Geometry;
        }
        catch (Exception)
        {
            // 路径文本有问题时不让它炸主窗口：那颗图标留空，别的地方照常。
            return null;
        }
    }
}
