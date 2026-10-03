using Windows.ApplicationModel.DataTransfer;

namespace Bodian.WinUI.Services;

/// <summary>系统剪贴板。抽出来是为了让 ViewModel 不直接依赖 WinUI 的静态 API。</summary>
public interface IClipboardService
{
    /// <summary>把文本放进剪贴板。失败时抛异常，由调用方决定怎么提示。</summary>
    void SetText(string text);
}

/// <inheritdoc cref="IClipboardService" />
public sealed class ClipboardService : IClipboardService
{
    /// <remarks>
    /// <b>必须在 UI 线程调用</b>：<see cref="Clipboard.SetContent"/> 会触碰剪贴板窗口，
    /// 后台线程调用会抛 <c>RPC_E_WRONG_THREAD</c>。命令是从按钮点击进来的，天然在 UI 线程。
    /// </remarks>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
