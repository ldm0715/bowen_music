using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<NavigationService> _logger;
    private readonly bool _diagnostics = Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1";

    private readonly NavigationStack<Page> _stack;
    private readonly Dictionary<Type, Page> _rootPages = [];
    private readonly LinkedList<Type> _rootRecency = new();
    private readonly Dictionary<Type, Page> _recycledDetails = [];

    private ContentControl? _host;
    private ContentControl? _shownHost;
    private Func<Page, ContentControl>? _selectHost;
    private Func<Page, Page, Task>? _beforeNavigate;
    private Page? _pendingPage;
    private float _pendingX;
    private float _pendingY;
    private bool _departing;
    private CoverTransitionAnimator.NavigationCover? _pendingCover;

    /// <summary>当前真正挂在宿主上的页面。用来判断「这次到底变没变」。</summary>
    private Page? _shown;

    public NavigationService(IServiceProvider services, ILogger<NavigationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        _logger = logger ?? NullLogger<NavigationService>.Instance;

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

    public void Attach(ContentControl host, Func<Page, ContentControl>? selectHost = null,
        Func<Page, Page, Task>? beforeNavigate = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _selectHost = selectHost;
        _beforeNavigate = beforeNavigate;
    }

    internal TPage GetDetail<TPage, TModel>(TModel model, Func<TModel, TPage> create)
        where TPage : Page, IReusableDetailPage<TModel>
    {
        if (_recycledDetails.TryGetValue(typeof(TPage), out var candidate)
            && !candidate.IsLoaded && candidate.Parent is null && !_stack.ContainsInstance(candidate))
        {
            _recycledDetails.Remove(typeof(TPage));
            PageVisualResources.Reset(candidate);
            CoverTransitionAnimator.Current?.ForgetPage(candidate);
            var page = (TPage)candidate;
            page.Rebind(model);
            return page;
        }
        return create(model);
    }

    public void ReleaseCachedPages()
    {
        _rootPages.Clear();
        _rootRecency.Clear();
        _recycledDetails.Clear();
    }

    public void Navigate<TPage>() where TPage : Page =>
        Show(_stack.Push(_stack.Current is TPage current && current is not INavigationIdentity
            ? current : _services.GetRequiredService<TPage>()));

    public void Navigate(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        Show(_stack.Push(page));
    }

    public void NavigateRoot<TPage>() where TPage : Page
    {
        // 先找当前返回栈里的已有无参页面；不要先构造一个 XAML 页面再被栈丢弃。
        var existing = _stack.Current is TPage current && current is not INavigationIdentity ? current
            : _stack.History.OfType<TPage>().FirstOrDefault(page => page is not INavigationIdentity);
        var type = typeof(TPage);
        if (existing is null && _rootPages.TryGetValue(type, out var cached)) existing = (TPage)cached;
        var page = existing ?? _services.GetRequiredService<TPage>();
        if (page is not INavigationIdentity)
        {
            _rootPages[type] = page;
            _rootRecency.Remove(type);
            _rootRecency.AddFirst(type);
            if (_rootPages.Count > 3 && _rootRecency.Last is { } oldest)
            {
                _rootPages.Remove(oldest.Value);
                _rootRecency.RemoveLast();
            }
        }
        Show(_stack.NavigateRoot(page), 0, 20);
    }

    public void NavigateRoot(Page page) => Show(_stack.NavigateRoot(page), 0, 20);

    public void Reset<TPage>() where TPage : Page
    {
        // 登录/登出重置时清缓存，避免旧账号数据被复用。
        _rootPages.Clear();
        _rootRecency.Clear();
        Show(_stack.Reset(_services.GetRequiredService<TPage>()), 0, 20);
    }

    public void GoBack()
    {
        // 栈空时 GoBack 返回 null，这里自然什么都不做。
        if (_stack.GoBack() is { } page)
        {
            Show(page, -28, 0);
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
    public void GoBackTo(Func<Page, bool> destination)
    {
        // 栈里可以跳过歌词页，但宿主只切一次，不调用中间页的进场/离场动画。
        if (_stack.GoBackTo(destination) is { } page) Show(page, -28, 0);
    }

    private void Show(Page next, float x = 28, float y = 0)
    {
        _pendingPage = next;
        _pendingX = x;
        _pendingY = y;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoBack)));
        // 返回栈立即前进，视觉离场只跑一次；连点返回或直接选页时提交最后的目标。
        if (_departing)
        {
            _pendingCover = null;
            return;
        }
        if (ReferenceEquals(_shown, next))
        {
            _pendingPage = null;
            return;
        }
        _pendingCover = CoverTransitionAnimator.Current?.PrepareNavigation(_shown, next, x < 0);
        if (_shown is not null && _beforeNavigate is not null)
        {
            var departure = _beforeNavigate(_shown, next);
            if (!departure.IsCompletedSuccessfully)
            {
                _departing = true;
                _ = CompleteDepartureAsync(departure);
                return;
            }
        }
        CommitPending();
    }

    private async Task CompleteDepartureAsync(Task departure)
    {
        try { await departure; }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "页面离场动画失败，直接完成导航");
        }
        finally { _departing = false; }
        // 窗口已经关闭时，不再挂载页面或重新开启播放。
        if (_host is not { IsLoaded: true })
        {
            _pendingPage = null;
            return;
        }
        CommitPending();
    }

    private void CommitPending()
    {
        if (_pendingPage is not { } next) return;
        var x = _pendingX;
        var y = _pendingY;
        _pendingPage = null;
        var sharedCover = _pendingCover;
        _pendingCover = null;
        if (ReferenceEquals(_shown, next))
        {
            // 离场途中又回到原页，只恢复视觉状态，不重复页面生命周期。
            Navigated?.Invoke(this, next);
            return;
        }
        ShowCore(next, x, y, sharedCover);
    }

    private void ShowCore(Page next, float x, float y, CoverTransitionAnimator.NavigationCover? sharedCover)
    {
        var started = Stopwatch.GetTimestamp();
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
        var previous = _shown;
        if (_shown is not null) AppMotion.Reset(_shown);
        Notify(_shown, leaving: true);

        var nextHost = _selectHost?.Invoke(next) ?? _host;
        // 常规页留在外壳中作为展开/收起的背景；生命周期仍按离场、进场各通知一次。
        if (_shownHost is not null && (ReferenceEquals(_shownHost, nextHost)
            || !ReferenceEquals(_shownHost, _host)))
        {
            _shownHost.Content = null;
        }

        PageVisualResources.Track(next);
        _shownHost = nextHost;
        _shownHost.Content = next;
        _shown = next;
        if (ReferenceEquals(_shownHost, _host))
            _ = AppMotion.EnterAsync(next, sharedCover is null ? x : 0, sharedCover is null ? y : 0);

        Notify(next, leaving: false);
        // 仅保留每种已离开返回栈的详情页一份，下一张歌单/专辑重用其布局。
        if (previous is IReusableDetailPage && !_stack.ContainsInstance(previous))
            _recycledDetails[previous.GetType()] = previous;

        Navigated?.Invoke(this, next);
        if (sharedCover is not null) _ = CoverTransitionAnimator.Current?.PlayNavigationAsync(sharedCover);
        if (_diagnostics) _logger.LogInformation("页面切换 {Page}：UI 处理 {Elapsed:F2} ms", next.GetType().Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
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
