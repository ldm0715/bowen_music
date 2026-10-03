using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 造一个跟随应用主题、并且收紧了内边距的对话框。
/// </summary>
/// <remarks>
/// <para>
/// <b>主题：</b>代码构造的 <see cref="ContentDialog"/> 不在可视树里，
/// <b>不会继承外壳上那个 <c>RequestedTheme</c></b> —— 不显式设就永远跟随系统，
/// 应用内切成深色时它还是一块白板。所以调用方必须把当前主题传进来
/// （外壳页面用 <c>Theme.RequestedTheme</c>，页面用 <c>ActualTheme</c>）。
/// </para>
/// <para>
/// <b>内边距：</b>WinUI 的对话框模板（<c>generic.xaml</c> 的 <c>DefaultContentDialogStyle</c>）
/// 给内容区与按钮区<b>各</b>留了一份 <c>ContentDialogPadding</c>（<c>24</c> 四边），
/// 两段叠起来，内容底下就凭空多出 48 的空档，按钮区自己也撑到 80 高 ——
/// 而里面只有两个扁按钮。这里收到上下 12。
/// </para>
/// <para>
/// 同一个模板还给了 <c>ContentDialogMinHeight = 184</c>：内容不够高时它会把对话框整个撑起来，
/// 多出的高度全堆在内容下方，看着就是「这一行下面怎么这么空」。一并解掉。
/// 宽度也一并收到 360（默认 <c>MaxWidth</c> 是 548，内容只有一栏时左右空一大片）。
/// </para>
/// <para>
/// <b>窄对话框用这个，宽内容自己覆写 <c>ContentDialogMaxWidth</c></b> ——
/// 编辑歌单那种带标签网格的内容 360 摆不下。
/// </para>
/// </remarks>
internal static class AppDialogs
{
    /// <summary>默认宽度上限。</summary>
    public const double DefaultMaxWidth = 360;

    public static ContentDialog Create(string title, XamlRoot xamlRoot, ElementTheme theme, double maxWidth = DefaultMaxWidth)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            RequestedTheme = theme,
            Title = title,
        };

        dialog.Resources["ContentDialogPadding"] = new Thickness(24, 12, 24, 12);
        dialog.Resources["ContentDialogMinHeight"] = 0d;
        dialog.Resources["ContentDialogMinWidth"] = 320d;
        dialog.Resources["ContentDialogMaxWidth"] = maxWidth;

        return dialog;
    }
}
