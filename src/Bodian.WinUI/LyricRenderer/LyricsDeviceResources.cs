using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;

namespace Bodian.WinUI.LyricRenderer;

/// <summary>
/// 设备相关资源的整体容器。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不逐个精确释放。</b> 一首歌 63 行就是 63 个 <see cref="Microsoft.Graphics.Canvas.Text.CanvasTextLayout"/>，
/// 再加上缓冲与效果，接近一百个 <c>IDisposable</c>。而且<b>设备丢失之后旧对象已经失效</b>，
/// 那时连 <c>Dispose</c> 本身都会抛 —— 逐个抢救在设备丢失场景下根本不可能正确。
/// 所以策略是<b>整体换代</b>：管好这一个容器的生命周期。
/// </para>
/// <para>
/// 所有成员都只能在渲染线程建、用、释放。
/// </para>
/// </remarks>
internal sealed class LyricsDeviceResources : IDisposable
{
    public LyricsDeviceResources(ICanvasResourceCreator resourceCreator, LyricsRenderSettings settings)
    {
        LayoutEngine = new LyricsLayoutEngine(settings);

        // 效果对象预建、只改属性。每帧 new 一个效果会同时分配托管与原生资源。
        Blur = new GaussianBlurEffect
        {
            BorderMode = EffectBorderMode.Soft,
            BlurAmount = 0,
        };
    }

    public LyricsLayoutEngine LayoutEngine { get; }


    /// <summary>
    /// 复用的远景模糊效果。
    /// </summary>
    /// <remarks>
    /// <b>效果可以复用，<see cref="CanvasCommandList"/> 不能。</b> command list 有个硬性限制：
    /// 一旦被当图像用过，就再也<b>不能</b>对它调 <c>CreateDrawingSession</c> ——
    /// 所以「复用一个行缓冲、每帧改内容」这条路是不存在的。变化的内容只能每帧新建一张，
    /// 用完即弃（参考实现也是这么做的）。
    /// </remarks>
    public GaussianBlurEffect Blur { get; }

    /// <summary>当前缓存的排版快照。换代时一并丢弃。</summary>
    public LyricsLayoutSnapshot? Snapshot { get; set; }

    public void Dispose()
    {
        Snapshot?.Dispose();
        Snapshot = null;

        Blur.Dispose();
        LayoutEngine.Dispose();
    }
}
