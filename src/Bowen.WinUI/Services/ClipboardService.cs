using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace Bowen.WinUI.Services;

/// <summary>系统剪贴板。抽出来是为了让 ViewModel 不直接依赖 WinUI 的静态 API。</summary>
public interface IClipboardService
{
    /// <summary>把文本放进剪贴板。失败时抛异常，由调用方决定怎么提示。</summary>
    void SetText(string text);

    /// <summary>
    /// 同时写入一张 PNG 与一段文本。
    /// </summary>
    /// <remarks>
    /// 歌词海报的「复制图片」需要图片和分享链接各一份：进聊天工具贴图，进记事本贴链接。
    /// </remarks>
    Task SetImageAndTextAsync(byte[] png, string text);
}

/// <inheritdoc cref="IClipboardService" />
public sealed class ClipboardService : IClipboardService
{
    /// <summary>
    /// 最近一次写入剪贴板的图片流。
    /// </summary>
    /// <remarks>
    /// 剪贴板对图片是延迟读取：系统在目标应用粘贴时才回头读这个流。
    /// 方法返回就释放会被贴成空图，所以留一份引用到最后一次复制。
    /// </remarks>
    private static InMemoryRandomAccessStream? _imageStream;

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

    /// <remarks>
    /// <b>同样必须在 UI 线程调用。</b> 图片流在系统实际粘贴之前不能释放，见 <see cref="_imageStream"/>。
    /// </remarks>
    public async Task SetImageAndTextAsync(byte[] png, string text)
    {
        ArgumentNullException.ThrowIfNull(png);
        ArgumentNullException.ThrowIfNull(text);

        var package = new DataPackage();
        package.SetText(text);

        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));

        Clipboard.SetContent(package);

        // 替换掉上一张的流；旧流可能仍被某个粘贴目标读着，交给 GC 回收即可。
        _imageStream = stream;
    }
}
