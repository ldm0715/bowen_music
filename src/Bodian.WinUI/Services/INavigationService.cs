using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Services;

/// <summary>
/// 页面切换。自研的一层薄封装，不引第三方导航框架。
/// </summary>
/// <remarks>
/// <para>
/// <b>刻意不用 <see cref="Frame.Navigate(Type)"/>。</b> 那个方法走 XAML 的类型解析 +
/// <c>Activator.CreateInstance</c>，要求页面有<b>无参构造</b>，与「构造函数注入」互斥 ——
/// 要么放弃 DI 改用服务定位器，要么放弃它。这里选后者，代价是页面的激活时机改走
/// <c>Loaded</c> 事件而不是 <c>OnNavigatedTo</c>。
/// </para>
/// <para>
/// 页面一律从容器解析，所以注册成 Transient 时每次都是新实例 —— 这一点很重要，
/// 复用同一个 <see cref="Page"/> 实例赋给 <c>Frame.Content</c> 会因为 Parent 未清而抛异常。
/// </para>
/// </remarks>
public interface INavigationService
{
    /// <summary>由宿主窗口在构造后调一次，把承载页面用的 <see cref="Frame"/> 交进来。</summary>
    void Attach(Frame frame);

    /// <summary>切到某个页面。</summary>
    void Navigate<TPage>() where TPage : Page;

    /// <summary>
    /// 切到某个页面并<b>清空导航状态</b>。登录成功、被动登出这类「不该退回去」的跳转用它。
    /// </summary>
    void Reset<TPage>() where TPage : Page;
}
