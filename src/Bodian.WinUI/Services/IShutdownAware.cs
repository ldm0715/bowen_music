namespace Bodian.WinUI.Services;

/// <summary>
/// 窗口正在关闭时要收尾的页面。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不复用 <see cref="INavigationAware.OnNavigatedFrom"/></b>：那条路的语义是「被换下」，
/// 页面在里头做的事（恢复音频、退出沉浸态）都是给「还活着继续用」准备的。
/// 关机时那些既多余、又可能反过来添乱 —— 比如把音频重新放起来。
/// </para>
/// <para>
/// <b>为什么需要这样一个钩子</b>：关窗口不走导航，页面拿不到任何离场通知。
/// 而 DI 容器会在 <c>_host.Dispose()</c> 时释放它捕获的瞬态 <see cref="IDisposable"/> ——
/// MV 页的 <c>MediaPlayer</c> 就是其中之一，它会在 <c>MediaPlayerElement</c> 还绑着它的时候
/// 被释放，原生对象的状态就崩了。症状是**播放 MV 时点关闭直接卡死**
/// （<c>docs/mv.md</c> §5 记过同一条约束，那次是在导航路径上修掉的，没覆盖关窗口这条路）。
/// </para>
/// <para>
/// <b>调用时机</b>：宿主必须在释放容器之前调 —— 见 <c>App.OnLaunched</c> 里的 <c>window.Closed</c>，
/// 那一句排在 <c>SmtcManager</c> 与音频引擎之前。
/// </para>
/// </remarks>
public interface IShutdownAware
{
    /// <summary>窗口正在关闭。<b>只做必须赶在容器释放之前做的事。</b></summary>
    void OnShuttingDown();
}
