using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.Services;

/// <summary>
/// 单实例：保证同一登录会话里只有一个波点在跑，第二次启动把已有实例唤到前台。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它。</b> 主窗口的 ✕ 改成了「关闭到托盘」，进程会一直活着。用户从开始菜单
/// 再点一次时如果照样起第二个进程，就会有两个托盘图标、两份播放引擎去抢同一个系统媒体
/// （SMTC）会话 —— 后者正是 <see cref="AppIdentity"/> 的注释里警告过的那种互相顶掉。
/// </para>
/// <para>
/// <b>为什么仍在 App 构造函数里判。</b> 自定义 Program 只负责在 XAML 初始化前读取
/// 输入法兼容偏好，然后复用原来的 COM 包装器、Application.Start 和同步上下文启动步骤。
/// 单实例判定保留在 App 构造函数，仍早于 DI 容器、主窗口和开始菜单快捷方式。
/// </para>
/// <para>
/// <b>为什么不用 WinAppSDK 自带的 <c>AppInstance</c>。</b> 三个额外负担：它的
/// <c>RedirectActivationToAsync</c> 是异步的而且官方明确警告不能阻塞 STA；
/// <c>Activated</c> 回调可能不在 UI 线程上；它**不负责把窗口带回前台** ——
/// 那一段 <c>ShowWindow + SetForegroundWindow</c> 一样得自己写。下面这三样正好全绕掉。
/// </para>
/// </remarks>
internal sealed class SingleInstanceCoordinator : IDisposable
{
    /// <summary>
    /// 主窗口上的子类标识。
    /// </summary>
    /// <remarks>
    /// 不能与同一窗口上别的子类重复 —— 重复会<b>把先装的那个顶掉</b>，而且是静默的。
    /// 主窗口上现有：1 = <c>WindowRenderActivity</c>，4 = <c>MainWindow</c> 的会话结束监视。
    /// </remarks>
    private const nuint SubclassId = 3;

    /// <summary>
    /// 互斥量名。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 从 <see cref="AppIdentity.AppUserModelId"/> 派生，不另写一个字面量 —— 与
    /// <see cref="AppIdentity"/> 上「这些值必须一致」是同一条纪律，免得两套身份机制各自漂移。
    /// </para>
    /// <para>
    /// 用 <c>Local\</c> 前缀（按登录会话）而不是 <c>Global\</c>：同一台机器上两个用户各跑一份
    /// 是合理的，与 AUMID 的「进程级身份」语义一致。
    /// </para>
    /// </remarks>
    private static readonly string MutexName = $@"Local\{AppIdentity.AppUserModelId}.SingleInstance";

    /// <summary>
    /// 跨进程唤醒用的消息 id。
    /// </summary>
    /// <remarks>
    /// <b>0 表示注册失败</b>，那时唤醒功能不可用（记警告，不影响启动）。
    /// 两边进程各自注册一次，同一会话里拿到同一个 id。
    /// </remarks>
    private static readonly uint ActivationMessage =
        NativeMethods.RegisterWindowMessage($"{AppIdentity.AppUserModelId}.Activate");

    /// <summary>
    /// 主实例持有的互斥量句柄。
    /// </summary>
    /// <remarks>
    /// <b>必须是静态字段。</b> 句柄一旦被 GC 回收，命名对象就随之消失，
    /// 下一个启动的实例会误判自己是主实例 —— 症状是「关掉一个，另一个就冒出来两个图标」。
    /// </remarks>
    private static Mutex? _primaryMutex;

    private readonly ILogger<SingleInstanceCoordinator> _logger;

    private nint _windowHandle;
    private NativeMethods.SubclassProc? _messageHandler;
    private bool _disposed;

    public SingleInstanceCoordinator(ILogger<SingleInstanceCoordinator>? logger = null)
        => _logger = logger ?? NullLogger<SingleInstanceCoordinator>.Instance;

    /// <summary>另一个实例请求本实例到前台。</summary>
    /// <remarks>
    /// 在 UI 线程上触发 —— 窗口消息只会在拥有该窗口的线程上派发，所以订阅方不必再切线程。
    /// </remarks>
    public event EventHandler? ActivationRequested;

    /// <summary>
    /// 尝试成为主实例。<c>false</c> 表示已经有实例在跑。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>只能在 <c>App</c> 构造函数最前面调</b>，早于任何窗口与 DI 容器。
    /// </para>
    /// <para>
    /// <c>initiallyOwned: false</c>：我们只用「这个命名对象存不存在」当信号，
    /// 不关心所有权，也就避开了「被放弃的互斥量」那套语义。
    /// </para>
    /// </remarks>
    internal static bool TryAcquirePrimary()
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName, out var createdNew);

        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _primaryMutex = mutex;
        return true;
    }

    /// <summary>
    /// 请已经在跑的那个实例把主窗口唤到前台。本进程随即应当退出。
    /// </summary>
    /// <remarks>
    /// <b>先让出前台权限再广播。</b> 此刻本进程是前台进程（用户刚点了快捷方式），
    /// 不把权限让出去的话，接收方的 <c>SetForegroundWindow</c> 会被系统拒绝 ——
    /// 症状是「双击了没反应」。传 <c>ASFW_ANY</c> 而不是自己的 pid：我们并不关心
    /// 谁来接这一棒。
    /// </remarks>
    internal static void SignalExistingInstance()
    {
        if (ActivationMessage == 0)
        {
            return;
        }

        NativeMethods.AllowSetForegroundWindow(NativeMethods.AsfwAny);
        NativeMethods.PostMessage(NativeMethods.HwndBroadcast, ActivationMessage, 0, 0);
    }

    /// <summary>
    /// 把主窗口挂上，开始接收唤醒广播。
    /// </summary>
    /// <remarks>
    /// 主窗口建好之后再调。窗口被隐藏时照样收得到 —— 广播会投递到隐藏的顶层窗口。
    /// </remarks>
    internal void AttachTo(nint windowHandle)
    {
        if (_disposed || _windowHandle != 0)
        {
            return;
        }

        if (ActivationMessage == 0)
        {
            _logger.LogWarning("注册单实例唤醒消息失败，第二次启动将无法唤起已有窗口");
            return;
        }

        _windowHandle = windowHandle;
        _messageHandler = OnWindowMessage;

        if (!NativeMethods.SetWindowSubclass(windowHandle, _messageHandler, SubclassId, 0))
        {
            _logger.LogWarning("装窗口子类失败，第二次启动将无法唤起已有窗口");

            _windowHandle = 0;
            _messageHandler = null;
        }
    }

    /// <summary>
    /// 收到唤醒广播。
    /// </summary>
    /// <remarks>
    /// 同一个进程里别的顶层窗（桌面歌词窗、小窗）没装子类，收到后走
    /// <c>DefSubclassProc</c>，没有副作用 —— 所以只有主窗口这一个会响应，不会重复触发。
    /// </remarks>
    private nint OnWindowMessage(nint window, uint message, nuint wParam, nint lParam,
        nuint subclassId, nuint referenceData)
    {
        if (message == ActivationMessage)
        {
            ActivationRequested?.Invoke(this, EventArgs.Empty);
        }

        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }

    /// <remarks>
    /// <b>刻意不释放那个互斥量。</b> 它的寿命必须等于进程寿命 —— 在收尾阶段把名字释放掉，
    /// 会让此刻新启动的实例自认是主实例，而本进程还在放歌。句柄交给进程退出去关。
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_windowHandle != 0 && _messageHandler is { } handler)
        {
            try
            {
                NativeMethods.RemoveWindowSubclass(_windowHandle, handler, SubclassId);
            }
            catch (Exception)
            {
                // 窗口正在销毁，移除失败是正常的；Windows 自己会清掉子类引用。
            }
        }

        _windowHandle = 0;
        _messageHandler = null;
    }
}
