using System.Runtime.InteropServices;

namespace Bodian.WinUI;

internal static class NativeMethods
{
    /// <summary>
    /// 显式设置进程的 AppUserModelID。影响任务栏分组与 toast 归属。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>必须在创建任何窗口之前调用。</b> unpackaged 应用没有 identity，这一步是替代品之一；
    /// 另一个（SMTC 面板显示应用名而非 exe 文件名）要靠开始菜单快捷方式，那是 P3 的事。
    /// </para>
    /// <para>
    /// 用 <c>DllImport</c> 而不是 <c>LibraryImport</c>：后者要求整个项目开
    /// <c>AllowUnsafeBlocks</c>，为一个 P/Invoke 打开 unsafe 不划算。
    /// </para>
    /// </remarks>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int SetCurrentProcessExplicitAppUserModelID(string appUserModelId);

    /// <summary>
    /// 取窗口所在显示器的 DPI（96 = 100%）。
    /// </summary>
    /// <remarks>
    /// <b>为什么需要它</b>：<c>AppWindow.MoveAndResize</c> 收的是**物理像素**，
    /// 而用户说的「窗口 1300×1000」是逻辑像素。不换算的话，125% 缩放下窗口会比预期小一圈。
    /// </remarks>
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hwnd);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ScreenToClient(nint window, ref NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();
}
