using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

// 接口在 Microsoft.UI.Composition 下，别名是为了让重写签名写得出来。
using ICompositionSupportsSystemBackdrop = Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 把窗口做成一块只显示 XAML 内容的透明玻璃。
/// </summary>
/// <remarks>
/// <para>
/// <b>透明不是一件事，是一组</b>，少一件就露底色或留白边：
/// </para>
/// <list type="number">
/// <item><c>DwmExtendFrameIntoClientArea</c> 把整窗纳入 DWM 合成；</item>
/// <item><c>DwmEnableBlurBehindWindow</c> 打开逐像素混合的通道；</item>
/// <item>拦下 <c>WM_ERASEBKGND</c> 自己填黑 —— 不做的话系统会把客户区擦成不透明；</item>
/// <item>一个全透明的系统背板画刷（能设上就设，见下）。</item>
/// </list>
/// <para>
/// <b>不能用 Mica / Acrylic 替代</b>：那两个是 DWM 材质，会在窗口背后铺一层系统画的东西，
/// 给不了「文字直接浮在桌面上」。
/// </para>
/// <para>
/// 窗口还必须带 <c>WS_EX_LAYERED</c>，那一步在窗口自己那里做。
/// </para>
/// </remarks>
public sealed class TransparentBackdrop : SystemBackdrop
{
    /// <summary>子类化用的标识，同一个窗口上别重复用。</summary>
    private const nuint SubclassId = 1;

    private readonly nint _windowHandle;
    private readonly ILogger _logger;
    private NativeMethods.SubclassProc? _messageHandler;
    private nint _eraseBrush;

    /// <param name="windowHandle">目标窗口的原生句柄。DWM 那几步要它。</param>
    /// <param name="logger">诊断用；背板设不上时要能看见原因，不能悄悄退化成不透明。</param>
    public TransparentBackdrop(nint windowHandle, ILogger? logger = null)
    {
        _windowHandle = windowHandle;
        _logger = logger ?? NullLogger.Instance;
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        ArgumentNullException.ThrowIfNull(connectedTarget);

        ConfigureDwm();
        HookEraseBackground();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop connectedTarget)
    {
        if (_messageHandler is not null)
        {
            NativeMethods.RemoveWindowSubclass(_windowHandle, _messageHandler, SubclassId);
            _messageHandler = null;
        }

        if (_eraseBrush != 0)
        {
            NativeMethods.DeleteObject(_eraseBrush);
            _eraseBrush = 0;
        }

        if (connectedTarget is not null)
        {
            connectedTarget.SystemBackdrop = null;
        }
    }

    /// <remarks>
    /// <b>这里刻意什么都不设。</b> 曾经试过给窗口挂一块全透明的背板画刷，三条路都堵死了：
    /// 接口这个属性的类型是 <c>Windows.UI.Composition.CompositionBrush</c>；
    /// <c>new Windows.UI.Composition.Compositor()</c> 在 WinUI 进程里抛异常（用的是 lifted 合成器）；
    /// 拿 WinUI 自己的 compositor 造画刷再 <c>As&lt;&gt;</c> 转换会 <c>InvalidCastException</c>。
    /// <b>而它根本不需要</b> —— 透明完全由下面那三步提供。留空是结论，不是没写完。
    /// </remarks>
    private void ConfigureDwm()
    {
        // 全零边距 = 整个客户区交给 DWM 合成。
        var margins = new NativeMethods.Margins();
        var extend = NativeMethods.DwmExtendFrameIntoClientArea(_windowHandle, ref margins);

        // 退化成一像素的模糊区域：这里只要「打开通道」，不要真的模糊。
        var region = NativeMethods.CreateRectRgn(-2, -2, -1, -1);
        var blur = new NativeMethods.BlurBehind
        {
            Flags = NativeMethods.DwmBlurBehindEnable | NativeMethods.DwmBlurBehindBlurRegion,
            Enable = 1,
            BlurRegion = region,
        };
        var enableBlur = NativeMethods.DwmEnableBlurBehindWindow(_windowHandle, ref blur);

        // DWM 会拷贝这个区域，调用返回后本进程的这份就可以放了。
        if (region != 0)
        {
            NativeMethods.DeleteObject(region);
        }

        _logger.LogInformation("桌面歌词 spike · DWM：扩帧=0x{Extend:X8} 模糊=0x{Blur:X8}", extend, enableBlur);
    }

    /// <remarks>
    /// 默认的背景擦除会用系统主题色把客户区填满，透明就没了。这里自己填黑并返回 1，
    /// 表示「已经擦过了」。这是整套办法里最容易漏的一步 —— 漏了就是窗口一片纯色。
    /// </remarks>
    private void HookEraseBackground()
    {
        _messageHandler = OnWindowMessage;

        if (!NativeMethods.SetWindowSubclass(_windowHandle, _messageHandler, SubclassId, 0))
        {
            _logger.LogWarning("桌面歌词 spike · 拦截 WM_ERASEBKGND 失败");
        }
    }

    private nint OnWindowMessage(nint window, uint message, nuint wParam, nint lParam,
        nuint subclassId, nuint referenceData)
    {
        if (message == NativeMethods.WmEraseBackground && NativeMethods.GetClientRect(window, out var rect))
        {
            if (_eraseBrush == 0)
            {
                _eraseBrush = NativeMethods.CreateSolidBrush(0);
            }

            NativeMethods.FillRect((nint)wParam, ref rect, _eraseBrush);
            return 1;
        }

        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }
}
