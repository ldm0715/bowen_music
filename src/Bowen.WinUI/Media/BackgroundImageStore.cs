using System.Runtime.InteropServices.WindowsRuntime;
using Bowen.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Bowen.WinUI.Media;

/// <summary>
/// 背景图片的导入、亮度估算与删除。歌词页与外壳共用同一套流程。
/// </summary>
/// <remarks>
/// <para>
/// <b>导入时按 EXIF 摆正并重新编码成 PNG。</b> 预览用 <c>BitmapImage</c>，它会应用 EXIF 方向；
/// 最终渲染可能走 Win2D 或普通 <c>Image</c>，两者都不一定应用。统一在导入时摆正，
/// 预览与最终背景读的是同一份像素。
/// </para>
/// <para>
/// <b>最长边 1920。</b> 4K 原图长期占一张大纹理；背景铺满窗口后 1920 已经足够。
/// </para>
/// </remarks>
public sealed class BackgroundImageStore
{
    private const int MaxEdge = 1920;

    private readonly ILogger<BackgroundImageStore> _logger;

    public BackgroundImageStore(ILogger<BackgroundImageStore>? logger = null)
        => _logger = logger ?? NullLogger<BackgroundImageStore>.Instance;

    /// <summary>
    /// 导入一张图片，返回应用目录下的文件名；失败返回 <c>null</c>。
    /// </summary>
    /// <param name="source">用户选中的原始文件。</param>
    /// <param name="prefix">文件名前缀，用于区分歌词页与外壳的图片。</param>
    public async Task<string?> ImportAsync(StorageFile source, string prefix)
    {
        ArgumentNullException.ThrowIfNull(source);

        var fileName = $"{prefix}-{Guid.NewGuid():N}.png";
        var destination = AppPaths.BackgroundFile(fileName);

        try
        {
            Directory.CreateDirectory(AppPaths.BackgroundsDirectory);

            using var input = await source.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(input);

            var longest = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            var scale = longest > MaxEdge ? (double)MaxEdge / longest : 1;
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
                ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
            };

            using var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            using var output = File.Create(destination);
            using var stream = output.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();

            return fileName;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "背景图片导入失败：{Name}", source.Name);
            TryDelete(destination);
            return null;
        }
    }

    /// <summary>
    /// 按图片平均亮度给一个初始暗度：把背景压到约 22% 亮度，亮图多压、暗图少压。
    /// </summary>
    /// <param name="source">原始文件。</param>
    /// <param name="maximum">该界面的暗度上限。</param>
    /// <param name="fallback">估算失败时的默认值。</param>
    public async Task<double> EstimateDimAsync(StorageFile source, double maximum, double fallback)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            using var stream = await source.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var data = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                new BitmapTransform { ScaledWidth = 32, ScaledHeight = 32 },
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            var pixels = data.DetachPixelData();
            if (pixels.Length < 4)
            {
                return fallback;
            }

            double sum = 0;

            for (var i = 0; i + 3 < pixels.Length; i += 4)
            {
                sum += (0.0722 * pixels[i]) + (0.7152 * pixels[i + 1]) + (0.2126 * pixels[i + 2]);
            }

            var luminance = sum / (pixels.Length / 4.0) / 255;
            var dim = luminance <= 0.22 ? 0.35 : 1 - (0.22 / luminance);

            return Math.Round(Math.Clamp(dim, 0.35, maximum), 2);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "背景图片亮度估算失败，使用默认暗度");
            return fallback;
        }
    }

    /// <summary>删除一张背景图片；只接受文件名，不接受路径。</summary>
    public void Delete(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        TryDelete(AppPaths.BackgroundFile(Path.GetFileName(fileName)));
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            // 旧图删不掉只留下一个文件，不影响本次选择。
            _logger.LogDebug(ex, "删除背景图片失败：{Path}", path);
        }
    }
}
