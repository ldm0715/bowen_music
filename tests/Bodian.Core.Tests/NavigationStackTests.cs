using Bodian.Core.Navigation;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 侧栏的导航语义。**这三条（栈底即根、换根截断、按身份判等）错了的表现是
/// 「侧栏高亮错一项」，肉眼极难发现** —— 所以每条都单独守着。
/// </summary>
public sealed class NavigationStackTests
{
    /// <summary>
    /// 测试用的页面替身，同时模拟两种判等方式。
    /// </summary>
    /// <remarks>
    /// <paramref name="BusinessId"/> 为 <c>null</c> 时是<b>无参根页</b>（发现、我喜欢的），
    /// 按 <paramref name="Kind"/> 判等，即「一种根页一个实例」；给了 id 就是<b>带参根页</b>
    /// （每个自建歌单详情），按 <c>(Kind, Id)</c> 判等。
    /// </remarks>
    private sealed record Screen(string Kind, string? BusinessId = null)
    {
        public object Identity => BusinessId is null ? Kind : (Kind, BusinessId);
    }

    private static NavigationStack<Screen> NewStack() => new(screen => screen.Identity);

    [Fact]
    public void ContainsInstance_ProtectsCurrentAndHistoryUntilTheyLeaveTheStack()
    {
        var stack = NewStack();
        var root = new Screen("发现");
        var detail = new Screen("歌单", "1");
        stack.NavigateRoot(root);
        stack.Push(detail);
        Assert.True(stack.ContainsInstance(root));
        Assert.True(stack.ContainsInstance(detail));
        Assert.False(stack.ContainsInstance(new Screen("歌单", "1")));
        stack.GoBack();
        Assert.True(stack.ContainsInstance(root));
        Assert.False(stack.ContainsInstance(detail));
        stack.NavigateRoot(new Screen("乐库"));
        Assert.False(stack.ContainsInstance(root));
    }

    // ── 栈底即根 ────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyStack_RootIsCurrent()
    {
        var stack = NewStack();
        var home = new Screen("我喜欢的");

        stack.Push(home);

        Assert.Same(home, stack.Current);
        Assert.Same(home, stack.Root);
        Assert.False(stack.CanGoBack);
    }

    [Fact]
    public void PushDetail_KeepsRoot()
    {
        var stack = NewStack();
        var root = new Screen("我喜欢的");
        var detail = new Screen("歌单详情", "99980832");

        stack.Push(root);
        stack.Push(detail);

        // 详情在栈上，「我喜欢的」仍是根 —— 这正是侧栏该继续高亮的那一项。
        Assert.Same(detail, stack.Current);
        Assert.Same(root, stack.Root);
        Assert.True(stack.CanGoBack);
    }

    // ── 压栈的判等 ──────────────────────────────────────────────────────────

    [Fact]
    public void Push_SameIdentity_IsIgnored()
    {
        var stack = NewStack();
        var first = new Screen("我喜欢的");

        stack.Push(first);
        var result = stack.Push(new Screen("我喜欢的"));

        // 连点两次同一个侧栏项不该压两层栈，也不该丢掉原来那个实例。
        Assert.Same(first, result);
        Assert.Same(first, stack.Current);
        Assert.False(stack.CanGoBack);
    }

    /// <summary>同一类型的两个带参根页**不是**同一个页面。</summary>
    [Fact]
    public void Push_SameKindDifferentBusinessId_IsADifferentPage()
    {
        var stack = NewStack();
        var first = new Screen("歌单详情", "111");
        var second = new Screen("歌单详情", "222");

        stack.Push(first);
        stack.Push(second);

        Assert.Same(second, stack.Current);
        Assert.True(stack.CanGoBack);

        // 按类型判等就会在这里出错：点第二个歌单会被当成「已经是这个页面」而静默不切换。
        Assert.Same(first, stack.Root);
    }

    // ── 换根 ────────────────────────────────────────────────────────────────

    [Fact]
    public void NavigateRoot_ToNewRoot_ClearsTheWholeStack()
    {
        var stack = NewStack();
        stack.Push(new Screen("我喜欢的"));
        stack.Push(new Screen("歌单详情", "111"));

        var mine = new Screen("创建的歌单");
        var result = stack.NavigateRoot(mine);

        Assert.Same(mine, result);
        Assert.Same(mine, stack.Current);
        Assert.Same(mine, stack.Root);
        Assert.False(stack.CanGoBack);
    }

    /// <summary>
    /// 这条是换根存在的**唯一理由**，也是压栈语义会出错的那个场景。
    /// </summary>
    [Fact]
    public void NavigateRoot_FromDetailToAnotherRoot_DoesNotLeaveTheOldRootAtTheBottom()
    {
        var stack = NewStack();
        var liked = new Screen("我喜欢的");
        stack.Push(liked);
        stack.Push(new Screen("歌单详情", "111"));

        var mined = new Screen("创建的歌单");
        stack.NavigateRoot(mined);

        // 压栈会得到 [我喜欢的, 歌单详情, 创建的歌单]，栈底仍是「我喜欢的」，侧栏高亮就错了。
        Assert.Same(mined, stack.Root);
        Assert.DoesNotContain(liked, stack.History);
    }

    [Fact]
    public void NavigateRoot_ToRootAlreadyInStack_TruncatesAndReusesInstance()
    {
        var stack = NewStack();
        var liked = new Screen("我喜欢的");
        stack.Push(liked);
        stack.Push(new Screen("歌单详情", "111"));

        var result = stack.NavigateRoot(new Screen("我喜欢的"));

        // 复用原实例：搜索结果、滚动位置都还在（那正是当初放弃 Frame 的理由）。
        Assert.Same(liked, result);
        Assert.Same(liked, stack.Current);
        Assert.False(stack.CanGoBack);
    }

    /// <summary>
    /// 带参根页复用已有实例：丢弃传进来的那个。
    /// </summary>
    /// <remarks>
    /// 这条同时守着「目标成为新的根」—— 目标<b>下面</b>的「创建的歌单」必须被丢掉，
    /// 否则栈底还是列表页，侧栏会高亮成列表页而不是刚点的那个歌单。
    /// </remarks>
    [Fact]
    public void NavigateRoot_SameBusinessId_ReusesTheExistingInstanceAndBecomesTheRoot()
    {
        var stack = NewStack();
        var detail = new Screen("歌单详情", "111");
        stack.Push(new Screen("创建的歌单"));
        stack.Push(detail);

        var result = stack.NavigateRoot(new Screen("歌单详情", "111"));

        Assert.Same(detail, result);
        Assert.Same(detail, stack.Current);
        Assert.Same(detail, stack.Root);
        Assert.False(stack.CanGoBack);
    }

    /// <summary>点自己正在看的那一项：它成为根，原来的父页不再可回退。</summary>
    [Fact]
    public void NavigateRoot_ToCurrentPage_MakesItTheRoot()
    {
        var stack = NewStack();
        var liked = new Screen("我喜欢的");
        var detail = new Screen("歌单详情", "111");
        stack.Push(liked);
        stack.Push(detail);

        var result = stack.NavigateRoot(detail);

        Assert.Same(detail, result);
        Assert.Same(detail, stack.Root);
        Assert.False(stack.CanGoBack);
    }

    // ── 回退与重置 ──────────────────────────────────────────────────────────

    [Fact]
    public void GoBack_RestoresThePreviousInstance()
    {
        var stack = NewStack();
        var root = new Screen("我喜欢的");
        stack.Push(root);
        stack.Push(new Screen("歌单详情", "111"));

        var restored = stack.GoBack();

        Assert.Same(root, restored);
        Assert.Same(root, stack.Current);
        Assert.False(stack.CanGoBack);
    }

    [Fact]
    public void GoBack_AtRoot_DoesNothing()
    {
        var stack = NewStack();
        var root = new Screen("我喜欢的");
        stack.Push(root);

        Assert.Null(stack.GoBack());
        Assert.Same(root, stack.Current);
    }

    [Fact]
    public void Reset_ClearsHistory()
    {
        var stack = NewStack();
        stack.Push(new Screen("我喜欢的"));
        stack.Push(new Screen("歌单详情", "111"));

        var login = new Screen("登录");
        var result = stack.Reset(login);

        Assert.Same(login, result);
        Assert.Same(login, stack.Root);
        Assert.False(stack.CanGoBack);
    }

    [Fact]
    public void GoBackTo_SkipsLyricsBetweenMvAndTheOriginalPage()
    {
        var stack = NewStack();
        var root = new Screen("发现");
        var detail = new Screen("歌单详情", "111");
        stack.Push(root);
        stack.Push(detail);
        stack.Push(new Screen("歌词"));
        stack.Push(new Screen("MV"));

        var restored = stack.GoBackTo(page => page.Kind is not "歌词" and not "MV");

        Assert.Same(detail, restored);
        Assert.Same(detail, stack.Current);
        Assert.Same(root, stack.Root);
        Assert.Equal(new[] { root }, stack.History);
        Assert.True(stack.CanGoBack);
    }

    [Fact]
    public void GoBackTo_MvOpenedDirectlyFromShell_ReturnsToTheSameShellInstance()
    {
        var stack = NewStack();
        var shell = new Screen("我喜欢的");
        stack.Push(shell);
        stack.Push(new Screen("MV"));

        Assert.Same(shell, stack.GoBackTo(page => page.Kind != "MV"));
        Assert.False(stack.CanGoBack);
    }

    [Fact]
    public void GoBackTo_NoMatchingAncestor_FallsBackToRoot()
    {
        var stack = NewStack();
        var root = new Screen("发现");
        stack.Push(root);
        stack.Push(new Screen("歌词"));
        stack.Push(new Screen("MV"));

        Assert.Same(root, stack.GoBackTo(_ => false));
        Assert.False(stack.CanGoBack);
    }

    [Fact]
    public void GoBackTo_AtRoot_DoesNotNavigate()
    {
        var stack = NewStack();
        var root = new Screen("发现");
        stack.Push(root);

        Assert.Null(stack.GoBackTo(_ => true));
        Assert.Same(root, stack.Current);
        Assert.Throws<ArgumentNullException>(() => stack.GoBackTo(null!));
    }

    // ── 参数校验 ────────────────────────────────────────────────────────────

    [Fact]
    public void NullIdentityFunction_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new NavigationStack<Screen>(null!));
    }

    [Fact]
    public void NullPage_Throws()
    {
        var stack = NewStack();

        Assert.Throws<ArgumentNullException>(() => stack.Push(null!));
        Assert.Throws<ArgumentNullException>(() => stack.NavigateRoot(null!));
        Assert.Throws<ArgumentNullException>(() => stack.Reset(null!));
    }
}
