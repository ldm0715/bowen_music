using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace Bodian.WinUI.Services;

/// <summary>与歌词窗绑定的原生边缘命中条；完整处理命中、鼠标捕获和离开后的光标恢复。</summary>
internal sealed class DesktopLyricsResizeCursor : IDisposable
{
    private const nuint SubclassId = 2;
    private const uint WmSetCursor = 0x0020, WmMouseActivate = 0x0021, WmCancelMode = 0x001F;
    private const uint WmNcHitTest = 0x0084, WmMouseMove = 0x0200, WmLeftButtonDown = 0x0201;
    private const uint WmLeftButtonUp = 0x0202, WmCaptureChanged = 0x0215, WmMouseLeave = 0x02A3;
    private const uint TrackLeave = 0x00000002, TrackCancel = 0x80000000;
    private readonly nint _edgeWindow;
    private readonly nint _resizeCursor;
    private readonly nint _arrowCursor;
    private readonly NativeMethods.SubclassProc _handler;
    private readonly Func<bool> _beginResize;
    private readonly Action _moveResize, _endResize;
    private RectInt32 _bounds;
    private bool _shown, _captured, _trackingLeave, _ownsCursor, _disposed;

    public DesktopLyricsResizeCursor(nint owner, Func<bool> beginResize, Action moveResize, Action endResize)
    {
        _beginResize = beginResize;
        _moveResize = moveResize;
        _endResize = endResize;
        _resizeCursor = NativeMethods.LoadCursor(0, (nint)32644); // IDC_SIZEWE，共享光标
        _arrowCursor = NativeMethods.LoadCursor(0, (nint)32512); // IDC_ARROW
        _handler = OnMessage;
        // WS_EX_LAYERED | TOOLWINDOW | NOACTIVATE；WS_POPUP | SS_NOTIFY | SS_BLACKRECT。
        // SS_NOTIFY 不可省，否则 STATIC 的默认 WM_NCHITTEST 返回 HTTRANSPARENT，拖动事件会落到下面。
        // 1/255 alpha 保留原生命中，光标和捕获都不经过 WinUI 输入线程。
        _edgeWindow = NativeMethods.CreateWindowEx(0x08080080, "STATIC", "", 0x80000104,
            0, 0, 1, 1, owner, 0, 0, 0);
        if (_edgeWindow == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建歌词拉伸边缘");
        if (!NativeMethods.SetLayeredWindowAttributes(_edgeWindow, 0, 1, 2)
            || !NativeMethods.SetWindowSubclass(_edgeWindow, _handler, SubclassId, 0))
        {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.DestroyWindow(_edgeWindow);
            throw new Win32Exception(error, "无法初始化歌词拉伸边缘");
        }
    }

    public void Update(RectInt32 bounds, bool visible)
    {
        if (_disposed) return;
        if (!visible)
        {
            var wasShown = _shown;
            _shown = false;
            FinishResize(commitPointer: false);
            StopLeaveTracking();
            RestoreArrow();
            if (wasShown) NativeMethods.ShowWindow(_edgeWindow, 0);
            return;
        }
        if (_shown && _bounds.X == bounds.X && _bounds.Y == bounds.Y
            && _bounds.Width == bounds.Width && _bounds.Height == bounds.Height) return;
        _bounds = bounds;
        _shown = true;
        NativeMethods.SetWindowPos(_edgeWindow, NativeMethods.HwndTopmost,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            NativeMethods.SwpNoActivate | 0x0040); // SWP_SHOWWINDOW
    }

    /// <summary>后台无焦点窗口的捕获范围有限；轮询补足移出边缘后的拖动与松开。</summary>
    public void PollResize()
    {
        if (_disposed || !_captured) return;
        if (NativeMethods.GetCapture() != _edgeWindow)
            FinishResize(commitPointer: false, releaseCapture: false);
        else if ((NativeMethods.GetAsyncKeyState(0x01) & 0x8000) == 0)
            FinishResize(commitPointer: true);
        else
            _moveResize();
    }

    private bool Contains(int x, int y)
        => _shown && x >= _bounds.X && x < _bounds.X + _bounds.Width
            && y >= _bounds.Y && y < _bounds.Y + _bounds.Height;

    private bool IsPointerInside()
        => NativeMethods.GetCursorPos(out var point) && Contains(point.X, point.Y);

    private void ApplyResizeCursor()
    {
        NativeMethods.SetCursor(_resizeCursor);
        _ownsCursor = true;
    }

    private void RestoreArrow()
    {
        if (!_ownsCursor) return;
        _ownsCursor = false;
        // 不覆盖其他窗口已经设置的 I-beam、手形等光标；只收回自己留下的拉伸光标。
        if (NativeMethods.GetCursor() == _resizeCursor) NativeMethods.SetCursor(_arrowCursor);
    }

    private void RefreshCursor()
    {
        if (_captured || IsPointerInside()) ApplyResizeCursor();
        else RestoreArrow();
    }

    private void StartLeaveTracking()
    {
        if (_trackingLeave) return;
        var tracking = new NativeMethods.MouseTracking
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.MouseTracking>(),
            Flags = TrackLeave,
            Window = _edgeWindow,
        };
        _trackingLeave = NativeMethods.TrackMouseEvent(ref tracking);
    }

    private void StopLeaveTracking()
    {
        if (!_trackingLeave) return;
        _trackingLeave = false;
        var tracking = new NativeMethods.MouseTracking
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.MouseTracking>(),
            Flags = TrackCancel | TrackLeave,
            Window = _edgeWindow,
        };
        NativeMethods.TrackMouseEvent(ref tracking);
    }

    private void FinishResize(bool commitPointer, bool releaseCapture = true)
    {
        if (!_captured) return;
        if (commitPointer) _moveResize();
        // 移动窗口可能同步关闭边缘，回调重入时不能重复结束手势。
        if (!_captured) return;
        _captured = false;
        if (releaseCapture && NativeMethods.GetCapture() == _edgeWindow) NativeMethods.ReleaseCapture();
        _endResize();
        RefreshCursor();
    }

    private nint OnMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        switch (message)
        {
            case WmNcHitTest:
                var packedPoint = (long)lParam;
                var x = (short)(packedPoint & 0xFFFF);
                var y = (short)((packedPoint >> 16) & 0xFFFF);
                return _captured || Contains(x, y) ? 1 : -1; // HTCLIENT / HTTRANSPARENT
            case WmSetCursor:
                if (_captured || (_shown && ((long)lParam & 0xFFFF) == 1 && IsPointerInside()))
                {
                    ApplyResizeCursor();
                    StartLeaveTracking();
                    return 1;
                }
                RestoreArrow();
                break;
            case WmMouseActivate:
                return 3; // MA_NOACTIVATE
            case WmLeftButtonDown:
                if (!_shown || _captured || !IsPointerInside() || !_beginResize()) return 0;
                _captured = true;
                NativeMethods.SetCapture(window);
                if (NativeMethods.GetCapture() != window)
                {
                    FinishResize(commitPointer: false, releaseCapture: false);
                    return 0;
                }
                ApplyResizeCursor();
                StartLeaveTracking();
                return 0;
            case WmMouseMove:
                if (_captured && (wParam & 0x0001) == 0)
                    FinishResize(commitPointer: true);
                else if (_captured)
                    _moveResize();
                RefreshCursor();
                if (IsPointerInside()) StartLeaveTracking();
                return 0;
            case WmLeftButtonUp:
                FinishResize(commitPointer: true);
                RefreshCursor();
                return 0;
            case WmMouseLeave:
                _trackingLeave = false;
                if (!_captured) RestoreArrow();
                return 0;
            case WmCancelMode:
                FinishResize(commitPointer: false);
                RestoreArrow();
                return 0;
            case WmCaptureChanged:
                FinishResize(commitPointer: false, releaseCapture: false);
                RestoreArrow();
                return 0;
        }
        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _shown = false;
        FinishResize(commitPointer: false);
        StopLeaveTracking();
        RestoreArrow();
        _disposed = true;
        NativeMethods.RemoveWindowSubclass(_edgeWindow, _handler, SubclassId);
        NativeMethods.DestroyWindow(_edgeWindow);
    }
}
