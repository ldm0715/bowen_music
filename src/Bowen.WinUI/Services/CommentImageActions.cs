using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Bowen.WinUI.Services;

/// <summary>评论图片的复制与另存。</summary>
/// <remarks>
/// 下载复用应用的 <see cref="HttpMessageHandler"/>，与封面缓存同一份连接池；业务通道按项目约定固定直连。
/// 复制前统一转成 PNG：评论图可能是 WebP，原样塞进剪贴板在部分粘贴目标里会失败。
/// </remarks>
public sealed class CommentImageActions
{
    private const long MaxBytes = 32L * 1024 * 1024;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;
    private readonly IClipboardService _clipboard;
    private readonly INoticeSink _notices;
    private readonly ILogger _logger;
    private readonly nint _windowHandle;

    public CommentImageActions(
        HttpMessageHandler handler,
        IClipboardService clipboard,
        INoticeSink notices,
        ILogger logger,
        nint windowHandle)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(logger);
        _http = new HttpClient(handler, disposeHandler: false) { Timeout = DownloadTimeout };
        _clipboard = clipboard;
        _notices = notices;
        _logger = logger;
        _windowHandle = windowHandle;
    }

    public async Task CopyAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            var bytes = await ReadBytesAsync(response);
            await _clipboard.SetImageAsync(await ToPngAsync(bytes));
            _notices.Show("图片已复制到剪贴板", NoticeSeverity.Success);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "复制评论图片失败：{Uri}", uri);
            _notices.Show("复制图片失败，请稍后再试。", NoticeSeverity.Error);
        }
    }

    public async Task SaveAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            var bytes = await ReadBytesAsync(response);
            var extension = ResolveExtension(response.Content.Headers.ContentType?.MediaType, uri);

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"评论图片_{DateTime.Now:yyyyMMdd_HHmmss}",
            };
            picker.FileTypeChoices.Add("图片", new List<string> { extension });
            InitializeWithWindow.Initialize(picker, _windowHandle);

            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            await FileIO.WriteBytesAsync(file, bytes);
            _notices.Show("图片已保存", NoticeSeverity.Success);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "保存评论图片失败：{Uri}", uri);
            _notices.Show("保存图片失败，请稍后再试。", NoticeSeverity.Error);
        }
    }

    private static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxBytes)
        {
            throw new InvalidDataException("图片超过大小上限。");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length == 0 || bytes.Length > MaxBytes)
        {
            throw new InvalidDataException("图片为空或超过大小上限。");
        }

        return bytes;
    }

    /// <summary>把任意可解码的图片转成 PNG，保证剪贴板粘到各处都能出图。</summary>
    private static async Task<byte[]> ToPngAsync(byte[] image)
    {
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input))
        {
            writer.WriteBytes(image);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        input.Seek(0);
        using var bitmap = await CanvasBitmap.LoadAsync(CanvasDevice.GetSharedDevice(), input);
        using var output = new InMemoryRandomAccessStream();
        await bitmap.SaveAsync(output, CanvasBitmapFileFormat.Png);
        output.Seek(0);

        var bytes = new byte[output.Size];
        using var reader = new DataReader(output.GetInputStreamAt(0));
        await reader.LoadAsync((uint)output.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static string ResolveExtension(string? mediaType, Uri uri) => mediaType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        _ => Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() switch
        {
            ".png" => ".png",
            ".jpg" or ".jpeg" => ".jpg",
            ".webp" => ".webp",
            ".gif" => ".gif",
            ".bmp" => ".bmp",
            _ => ".jpg",
        },
    };
}
