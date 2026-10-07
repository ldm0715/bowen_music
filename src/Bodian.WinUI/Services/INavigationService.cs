using System.ComponentModel;
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
/// 带参数的根页（如每个自建歌单的详情）用它声明「我是哪一个」。
/// </summary>
/// <remarks>
/// <para>
/// <b>不实现它的页面按类型判等</b>，即「一种根页只有一个实例」—— 无参根页（发现、我喜欢的）
/// 正是这种情况，不必写。
/// </para>
/// <para>
/// <b>必须实现它的是「同一类型、多个实例」的根页</b>：每个自建歌单详情都是
/// <c>PlaylistDetailPage</c>，按类型判等会让「点第二个歌单」被当成「已经是这个页面」而
/// 静默不切换。
/// </para>
/// </remarks>
public interface INavigationIdentity
{
    /// <summary>同一根页之间互相判等的依据。通常是 <c>(类型, 业务 id)</c> 这样的元组。</summary>
    object NavigationIdentity { get; }
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
/// 未清而抛异常；<c>ContentControl</c> 没有这个包袱。
/// </para>
/// <para>
/// <b>栈本身不在这里</b>：语义（换根 / 截断复用 / 栈底即根）在
/// <see cref="Bodian.Core.Navigation.NavigationStack{T}"/>，本接口的实现只负责
/// 「换内容 + 通知页面」。那是为了让侧栏语义能单测。
/// </para>
/// </remarks>
public interface INavigationService : INotifyPropertyChanged
{
    /// <summary>释放侧栏的闲置页面缓存；当前页和返回栈继续由导航栈持有。</summary>
    void ReleaseCachedPages();

    /// <summary>由宿主窗口在构造后调一次，把承载页面用的 <see cref="ContentControl"/> 交进来。</summary>
    void Attach(ContentControl host, Func<Page, ContentControl>? selectHost = null,
        Func<Page, Page, Task>? beforeNavigate = null);

    /// <summary>
    /// 切到某个页面（<b>压栈</b>，用于详情页）。当前页已经是同一个身份时什么都不做。
    /// </summary>
    /// <remarks>当前已经是该页面时什么都不做 —— 否则连点两次「词」会把同一个页面压两次栈。</remarks>
    void Navigate<TPage>() where TPage : Page;

    /// <summary>
    /// 切到某个页面（<b>压栈</b>），用调用方已经构造好的实例。
    /// </summary>
    /// <remarks>
    /// 带参详情页（榜详情、歌单详情）走这个重载 —— 页面构造要业务 id，DI 解析不出来。
    /// 与 <see cref="NavigateRoot(Page)"/> 的区别只在于是压栈还是换根：
    /// 详情页压栈（侧栏高亮留在原来的根），侧栏项才换根。
    /// </remarks>
    void Navigate(Page page);

    /// <summary>
    /// 切到某个根页（<b>换根</b>，用于侧栏点击）。<b>目标是新的根，历史全部丢弃。</b>
    /// </summary>
    void NavigateRoot<TPage>() where TPage : Page;

    /// <summary>
    /// 切到某个根页（<b>换根</b>），用调用方已经构造好的实例。
    /// </summary>
    /// <remarks>
    /// 带参根页（自建歌单详情）走这个重载 —— 页面构造要 <c>playlistId</c>，DI 解析不出来。
    /// 目标身份已在栈里时<b>丢弃传入的实例、复用已有的那个</b>（状态不丢）。
    /// </remarks>
    void NavigateRoot(Page page);

    /// <summary>
    /// 切到某个页面并<b>清空导航状态</b>。登录成功、被动登出这类「不该退回去」的跳转用它。
    /// </summary>
    void Reset<TPage>() where TPage : Page;

    /// <summary>还能不能返回。导航状态更新时通过 <see cref="INotifyPropertyChanged"/> 通知绑定。</summary>
    bool CanGoBack { get; }

    /// <summary>当前页。</summary>
    Page? Current { get; }

    /// <summary>
    /// 根页：侧栏该高亮的那一项。**栈底即根**（栈空时就是当前页）。
    /// </summary>
    /// <remarks>
    /// 从「我喜欢的」点进一个歌单详情时，<see cref="Current"/> 是详情页、<see cref="Root"/>
    /// 仍是「我喜欢的」—— 侧栏要跟着高亮的是后者。
    /// </remarks>
    Page? Root { get; }

    /// <summary>回到上一个页面。栈空时什么都不做。</summary>
    void GoBack();

    /// <summary>跳过中间页面，直接返回最近符合条件的页面。</summary>
    void GoBackTo(Func<Page, bool> destination);

    /// <summary>
    /// 每次当前页真的变了之后触发，参数是新的当前页。
    /// </summary>
    /// <remarks>
    /// <b>页面没有真的变化时不触发</b>（重复点同一个侧栏项、<c>Navigate</c> 到当前页），
    /// 订阅方可以放心把它当作「界面需要重新同步」的唯一信号。
    /// </remarks>
    event EventHandler<Page>? Navigated;
}
