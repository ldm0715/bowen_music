namespace Bodian.Core.Tests.Support;

/// <summary>
/// 同步把进度上报记下来的 <see cref="IProgress{T}"/>。
/// </summary>
/// <remarks>
/// <para>
/// <b>测试里不要用 <c>Progress&lt;T&gt;</c> 当记录器。</b> 它把回调投递到<b>当前同步上下文</b>；
/// 测试里没有上下文，于是退化成往线程池排队 —— 回调什么时候跑、跑没跑完，都不是 <c>await</c>
/// 返回时能保证的事。
/// </para>
/// <para>
/// 原本两处测试的写法是「<c>await Task.Yield()</c> 之后断言收齐了几条」，那等于赌线程池先执行谁：
/// 实测 <c>LikedSongsServiceTests</c> 那条三次里挂一次，表现为少收一条上报。
/// 这里的 <see cref="Report"/> 直接同步调用，断言在 <c>await</c> 返回后立刻成立。
/// </para>
/// <para>
/// <b>这不是产品代码的问题</b>：界面上的 <c>Progress&lt;T&gt;</c> 是在 UI 线程建的，
/// 投递回 UI 线程正是它该有的行为。
/// </para>
/// </remarks>
internal sealed class RecordingProgress<T> : IProgress<T>
{
    private readonly List<T> _reports = [];

    /// <summary>按上报的先后记下来的内容。</summary>
    public IReadOnlyList<T> Reports => _reports;

    public void Report(T value) => _reports.Add(value);
}
