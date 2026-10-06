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

    [DllImport("user32.dll", EntryPoint = "LoadCursorW", ExactSpelling = true)]
    internal static extern nint LoadCursor(nint instance, nint resourceId);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint SetCursor(nint cursor);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint SetCapture(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint GetCapture();

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint GetCursor();

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern short GetAsyncKeyState(int key);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseTracking
    {
        internal uint Size;
        internal uint Flags;
        internal nint Window;
        internal uint HoverTime;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TrackMouseEvent(ref MouseTracking tracking);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint window);

    internal delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam,
        nuint subclassId, nuint referenceData);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint subclassId, nuint referenceData);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint subclassId);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    internal static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPosition
    {
        internal nint Window;
        internal nint InsertAfter;
        internal int X, Y, Width, Height;
        internal uint Flags;
    }

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", ExactSpelling = true)]
    internal static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    internal static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    // ── 单实例唤醒 ──────────────────────────────────────────────────────────
    // 命名互斥量判出「已经有实例」之后，靠下面三个把已有实例的主窗口唤到前台。

    /// <summary><c>HWND_BROADCAST</c>：投给系统里所有顶层窗口。</summary>
    /// <remarks>
    /// Win32 文档明确广播**也会投递到隐藏的顶层窗口**（隐藏不等于禁用），
    /// 所以主窗口被 <c>AppWindow.Hide()</c> 藏到托盘之后照样收得到。
    /// 子窗口收不到 —— 我们正好只需要顶层窗口。
    /// </remarks>
    internal static readonly nint HwndBroadcast = new(0xFFFF);

    /// <summary><c>ASFW_ANY</c>：把抢前台的权限让给任意进程。</summary>
    internal const uint AsfwAny = unchecked((uint)-1);

    /// <summary>
    /// 注册一条自定义消息，返回它的 id。
    /// </summary>
    /// <remarks>
    /// <b>发送方与接收方都要调一次</b> —— 同一个字符串在同一会话里拿到同一个 id。
    /// 这是 Win32 为 <c>HWND_BROADCAST</c> 指定的用法（见 <c>PostMessageW</c> 的文档：
    /// 「需要广播的应用应当用 <c>RegisterWindowMessage</c> 取得一条唯一消息」），
    /// 直接用自定的 <c>WM_APP+n</c> 会撞上别的应用的同号消息。
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern uint RegisterWindowMessage(string message);

    /// <summary>
    /// 投递消息，不等对方处理。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="SendMessage"/> 的区别在这里是要害：<c>SendMessage</c> 会**阻塞到对方处理完**，
    /// 而广播是对系统里每一个顶层窗口各阻塞一次 —— 其中任何一个卡住都会把本进程一起拖住。
    /// 唤醒只需要「送到」，不要结果，所以用 Post。
    /// </remarks>
    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    /// <summary>
    /// 允许指定进程把窗口抢到前台。
    /// </summary>
    /// <remarks>
    /// <b>不调它接收方的 <c>SetForegroundWindow</c> 会被系统拒绝</b>，症状是
    /// 「点了托盘图标/双击了快捷方式，主窗口没起来」。发送方此刻是前台进程，
    /// 由它把权限让出去才成立 —— 传 <see cref="AsfwAny"/> 表示让给任意进程。
    /// </remarks>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllowSetForegroundWindow(uint processId);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect { internal int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPlacement
    {
        internal uint Length, Flags, ShowCommand;
        internal NativePoint MinimumPosition, MaximumPosition;
        internal NativeRect NormalPosition;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(nint window, ref WindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPlacement(nint window, ref WindowPlacement placement);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    /// <summary>
    /// 设置 DWM 窗口属性。桌面歌词窗用它去掉圆角与那圈细边框。
    /// </summary>
    /// <remarks>
    /// <c>DWMWA_WINDOW_CORNER_PREFERENCE</c> 是 <b>Win11 22000+</b> 才认的属性，
    /// Win10 上传入后返回失败且什么也不做 —— 调用方必须容忍失败，不能当异常处理。
    /// </remarks>
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    internal static extern int DwmSetWindowAttribute(nint window, uint attribute, ref int value, int size);

    // ── 窗口样式与 SetWindowPos 的常量 ──────────────────────────────────────
    // 桌面歌词窗按位改这些样式，写名字比写魔数好核对。

    /// <summary><c>GWL_STYLE</c>。</summary>
    internal const int GwlStyle = -16;

    /// <summary><c>GWL_EXSTYLE</c>。</summary>
    internal const int GwlExStyle = -20;

    /// <summary>分层窗口。逐像素透明的前提。</summary>
    internal const long WsExLayered = 0x00080000L;

    /// <summary>鼠标穿透：窗口收不到命中测试，点击落到下面的窗口。</summary>
    internal const long WsExTransparent = 0x00000020L;

    /// <summary>工具窗口：不出现在 Alt+Tab 与任务栏。</summary>
    internal const long WsExToolWindow = 0x00000080L;

    /// <summary>不抢焦点。</summary>
    internal const long WsExNoActivate = 0x08000000L;

    /// <summary>置顶。</summary>
    internal const long WsExTopmost = 0x00000008L;

    /// <summary>标题栏与边框。</summary>
    internal const long WsCaption = 0x00C00000L;

    /// <summary>可调边框。</summary>
    internal const long WsThickFrame = 0x00040000L;

    /// <summary>对话框边框。透明窗在 SDR 下那圈细白边就是它画出来的。</summary>
    internal const long WsDlgFrame = 0x00400000L;

    /// <summary><c>HWND_TOPMOST</c>。</summary>
    internal static readonly nint HwndTopmost = new(-1);

    /// <summary><c>SWP_NOSIZE</c>。</summary>
    internal const uint SwpNoSize = 0x0001;

    /// <summary><c>SWP_NOMOVE</c>。</summary>
    internal const uint SwpNoMove = 0x0002;

    /// <summary><c>SWP_NOZORDER</c>。</summary>
    internal const uint SwpNoZOrder = 0x0004;

    /// <summary><c>SWP_NOACTIVATE</c>。</summary>
    internal const uint SwpNoActivate = 0x0010;

    /// <summary><c>SWP_FRAMECHANGED</c>：让上面刚改的样式立刻生效。</summary>
    internal const uint SwpFrameChanged = 0x0020;

    /// <summary><c>SW_RESTORE</c>。把最小化的窗口还原成原来的大小与位置。</summary>
    internal const int SwRestore = 9;

    /// <summary><c>GA_ROOT</c>。</summary>
    internal const uint GaRoot = 2;

    /// <summary><c>DWMWA_WINDOW_CORNER_PREFERENCE</c>。</summary>
    internal const uint DwmwaWindowCornerPreference = 33;

    /// <summary><c>DWMWCP_DONOTROUND</c>。</summary>
    internal const int DwmcpDoNotRound = 1;

    /// <summary><c>WM_ERASEBKGND</c>。</summary>
    internal const uint WmEraseBackground = 0x0014;

    /// <summary>
    /// <c>WM_QUERYENDSESSION</c>：系统正在注销或关机。
    /// </summary>
    /// <remarks>
    /// 主窗口靠它区分「用户点了 ✕」与「系统要关机」—— 后者必须放行关闭，
    /// 否则会变成「这个应用阻止了关机」。见 <c>MainWindow.OnSessionWatchMessage</c>。
    /// </remarks>
    internal const uint WmQueryEndSession = 0x0011;

    /// <summary><c>DWM_BB_ENABLE</c>。</summary>
    internal const uint DwmBlurBehindEnable = 0x0001;

    /// <summary><c>DWM_BB_BLURREGION</c>。</summary>
    internal const uint DwmBlurBehindBlurRegion = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Margins
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    /// <remarks>
    /// <c>Enable</c> 与 <c>TransitionOnMaximized</c> 是 Win32 的 <c>BOOL</c>（4 字节），
    /// 用 <c>int</c> 而不是 <c>bool</c>，免得踩布局与默认封送方式的坑。
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BlurBehind
    {
        internal uint Flags;
        internal int Enable;
        internal nint BlurRegion;
        internal int TransitionOnMaximized;
    }

    /// <summary>
    /// 把窗口边框区域扩成整窗「玻璃」。
    /// </summary>
    /// <remarks>全零边距 = 整个客户区都参与 DWM 合成，这是透明的前提之一。</remarks>
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    internal static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);

    /// <summary>
    /// 开启窗口背后的模糊（这里只借它打开逐像素混合的通道，不真要模糊）。
    /// </summary>
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    internal static extern int DwmEnableBlurBehindWindow(nint window, ref BlurBehind blurBehind);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    internal static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    internal static extern nint CreateSolidBrush(int color);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "FillRect", ExactSpelling = true)]
    internal static extern int FillRect(nint deviceContext, ref NativeRect rect, nint brush);
}
