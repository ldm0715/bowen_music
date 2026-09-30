using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Services;

/// <summary>
/// 想知道自己什么时候被切进来、切出去。由 <see cref="INavigationService"/> 调用。
/// </summary>
/// <remarks>
/// <b>不靠 <c>Loaded</c>/<c>Unloaded</c> 这些控件生命周期事件。</b> 那两个事件的触发时机由
/// 布局系统决定，页面被换下时不一定按预期成对触发；而「歌词页活跃时才取词」这条
/// 直接决定要不要给第三方服务发请求，不能建立在一个不确定的时机上。
/// </remarks>
public interface INavigationAware
{
    /// <summary>已经成为当前页面。</summary>
    void OnNavigatedTo();

    /// <summary>不再是当前页面（被换下或被清栈）。</summary>
    void OnNavigatedFrom();
}

/// <summary>
/// 页面切换。自研的一层薄封装，不引第三方导航框架。
/// </summary>
/// <remarks>
/// <para>
/// <b>刻意不用 <see cref="Frame.Navigate(Type)"/>。</b> 那个方法走 XAML 的类型解析 +
/// <c>Activator.CreateInstance</c>，要求页面有<b>无参构造</b>，与「构造函数注入」互斥 ——
/// 要么放弃 DI 改用服务定位器，要么放弃它。这里选后者，代价是页面的激活时机改走
/// <see cref="INavigationAware"/> 而不是 <c>OnNavigatedTo</c>。
/// </para>
/// <para>
/// <b>宿主是 <see cref="ContentControl"/> 而不是 <see cref="Frame"/>。</b> 返回时要恢复的是
/// <b>原来那个页面实例</b>（重建 <c>SearchPage</c> 会把搜索结果全丢掉 —— 这正是 P4 当初
/// 把歌词做成覆盖层的原因）。而把同一个实例重复赋给 <c>Frame.Content</c> 会因为 Parent
/// 未清而抛异常；<c>ContentControl</c> 没有这个包袱。全仓本来就没有一处
/// <c>Frame.Navigate</c>，那个 <c>Frame</c> 一直只当 <c>ContentControl</c> 用。
/// </para>
/// </remarks>
public interface INavigationService
{
    /// <summary>由宿主窗口在构造后调一次，把承载页面用的 <see cref="ContentControl"/> 交进来。</summary>
    void Attach(ContentControl host);

    /// <summary>
    /// 切到某个页面。
    /// </summary>
    /// <remarks>当前已经是该类型时什么都不做 —— 否则连点两次「词」会把同一个页面压两次栈。</remarks>
    void Navigate<TPage>() where TPage : Page;

    /// <summary>
    /// 切到某个页面并<b>清空导航状态</b>。登录成功、被动登出这类「不该退回去」的跳转用它。
    /// </summary>
    void Reset<TPage>() where TPage : Page;

    /// <summary>还能不能返回。</summary>
    bool CanGoBack { get; }

    /// <summary>回到上一个页面。栈空时什么都不做。</summary>
    void GoBack();
}
