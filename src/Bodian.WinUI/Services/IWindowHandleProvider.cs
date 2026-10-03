namespace Bodian.WinUI.Services;

/// <summary>
/// 提供主窗口的句柄。
/// </summary>
/// <remarks>
/// <para>
/// WinUI 3 桌面端的 <see cref="Windows.Storage.Pickers.FileOpenPicker"/> 之类
/// 需要在显示前绑定宿主窗口（<c>WinRT.Interop.InitializeWithWindow.Initialize</c>），
/// 否则一打开就抛 —— 而句柄只有 <c>MainWindow</c> 拿得到。
/// </para>
/// <para>
/// 与 <see cref="IPlaylistLibrarySink"/> 同一种接线：外壳实现并注册成单例。
/// </para>
/// </remarks>
public interface IWindowHandleProvider
{
    /// <summary>主窗口的原生句柄。</summary>
    nint WindowHandle { get; }
}
