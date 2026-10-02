using System.ComponentModel;
using Bodian.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Services;

/// <inheritdoc cref="INavigationService" />
/// <remarks>
/// 这一层只做三件事：把页面内容换进宿主、按固定顺序通知页面、广播
/// <see cref="Navigated"/>。<b>栈的语义全在
/// <see cref="Bodian.Core.Navigation.NavigationStack{T}"/> 里</b>，本类不自己判等、
/// 不自己截断 —— 那些是能单测的部分，不该埋在需要 UI 线程才能跑起来的代码里。
/// </remarks>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;

    private readonly NavigationStack<Page> _stack;

    private ContentControl? _host;
    private ContentControl? _shownHost;
    private Func<Page, ContentControl>? _selectHost;

    /// <summary>当前真正挂在宿主上的页面。用来判断「这次到底变没变」。</summary>
    private Page? _shown;

    public NavigationService(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;

        // 身份：带参根页自己声明（每个自建歌单详情各是一个根），其余按类型判等 ——
        // 「发现」「我喜欢的」这类无参根页一种只有一个实例。
        _stack = new NavigationStack<Page>(
            page => page is INavigationIdentity identified
                ? identified.NavigationIdentity
                : page.GetType());
    }

    public event EventHandler<Page>? Navigated;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CanGoBack => _stack.CanGoBack;

    public Page? Current => _stack.Current;

    public Page? Root => _stack.Root;

    public void Attach(ContentControl host, Func<Page, ContentControl>? selectHost = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _selectHost = selectHost;
    }

    public void Navigate<TPage>() where TPage : Page =>
        Show(_stack.Push(_services.GetRequiredService<TPage>()));

    public void Navigate(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        Show(_stack.Push(page));
    }

    public void NavigateRoot<TPage>() where TPage : Page =>
        Show(_stack.NavigateRoot(_services.GetRequiredService<TPage>()));

    public void NavigateRoot(Page page) => Show(_stack.NavigateRoot(page));

    public void Reset<TPage>() where TPage : Page =>
        Show(_stack.Reset(_services.GetRequiredService<TPage>()));

    public void GoBack()
    {
        // 栈空时 GoBack 返回 null，这里自然什么都不做。
        if (_stack.GoBack() is { } page)
        {
            Show(page);
        }
    }

    /// <summary>
    /// 把某一页显示出来。
    /// </summary>
    /// <remarks>
    /// <b>页面没变就跳过页面生命周期</b>：重复点同一个侧栏项、<c>Navigate</c> 到当前页，都会走到这里
    /// 而目标与已在显示的是同一个实例。跳过意味着既不做内容赋值，也不发
    /// <see cref="INavigationAware.OnNavigatedFrom"/> / <see cref="INavigationAware.OnNavigatedTo"/>
    /// —— 后者会让页面白白重取一次数据。
    /// </remarks>
    private void Show(Page next)
    {
        // 换根可能只清空历史、继续显示同一实例；返回按钮仍必须同步为不可用。
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoBack)));

        if (ReferenceEquals(_shown, next))
        {
            return;
        }

        if (_host is null)
        {
            // 抛明确的异常，而不是等 NullReferenceException —— 这个错误信息直接指出该怎么修。
            throw new InvalidOperationException(
                $"导航到 {next.GetType().Name} 之前必须先调用 {nameof(Attach)}(ContentControl)。");
        }

        // 先通知离场，再换内容，最后通知进场 —— 顺序固定，页面可以放心在
        // OnNavigatedFrom 里释放、在 OnNavigatedTo 里重建。
        Notify(_shown, leaving: true);

        if (_shownHost is not null)
        {
            _shownHost.Content = null;
        }

        _shownHost = _selectHost?.Invoke(next) ?? _host;
        _shownHost.Content = next;
        _shown = next;

        Notify(next, leaving: false);

        Navigated?.Invoke(this, next);
    }

    private static void Notify(Page? page, bool leaving)
    {
        if (page is not INavigationAware aware)
        {
            return;
        }

        if (leaving)
        {
            aware.OnNavigatedFrom();
        }
        else
        {
            aware.OnNavigatedTo();
        }
    }
}
