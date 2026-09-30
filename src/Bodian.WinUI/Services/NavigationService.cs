using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Services;

/// <inheritdoc cref="INavigationService" />
public sealed class NavigationService(IServiceProvider services) : INavigationService
{
    /// <summary>
    /// 返回栈。存的是<b>页面实例</b>，不是类型 —— 返回时要把原来那个实例挂回去，
    /// 这样它自己的 ViewModel 与状态（搜索结果之类）原样还在。
    /// </summary>
    private readonly List<Page> _stack = [];

    private ContentControl? _host;

    public bool CanGoBack => _stack.Count > 0;

    public void Attach(ContentControl host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    public void Navigate<TPage>() where TPage : Page
    {
        // 已经是这个页面就别再压一次栈，否则连点按钮会把同一个页面叠好几层。
        if (Current is TPage)
        {
            return;
        }

        if (Current is { } current)
        {
            _stack.Add(current);
        }

        Swap(services.GetRequiredService<TPage>());
    }

    public void Reset<TPage>() where TPage : Page
    {
        _stack.Clear();
        Swap(services.GetRequiredService<TPage>());
    }

    public void GoBack()
    {
        if (_stack.Count == 0)
        {
            return;
        }

        var previous = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);

        Swap(previous);
    }

    private Page? Current => _host?.Content as Page;

    private void Swap(Page next)
    {
        if (_host is null)
        {
            // 抛明确的异常，而不是等 NullReferenceException —— 这个错误信息直接指出该怎么修。
            throw new InvalidOperationException(
                $"导航到 {next.GetType().Name} 之前必须先调用 {nameof(Attach)}(ContentControl)。");
        }

        // 先通知离场，再换内容，最后通知进场 —— 顺序固定，页面可以放心在
        // OnNavigatedFrom 里释放、在 OnNavigatedTo 里重建。
        Notify(Current, leaving: true);

        _host.Content = next;

        Notify(next, leaving: false);
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
