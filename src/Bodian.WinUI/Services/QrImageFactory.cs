using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;

namespace Bodian.WinUI.Services;

/// <summary>把扫码内容渲染成可显示的二维码图片。</summary>
public interface IQrImageFactory
{
    /// <summary>渲染二维码。<paramref name="pixelsPerModule"/> 是每个码元的像素数。</summary>
    ImageSource Create(string content, int pixelsPerModule = 8);
}

/// <inheritdoc cref="IQrImageFactory" />
public sealed class QrImageFactory : IQrImageFactory
{
    public ImageSource Create(string content, int pixelsPerModule = 8)
    {
        ArgumentException.ThrowIfNullOrEmpty(content);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);

        // ★ 只用库自带的渲染器，绝不自己遍历 QRCodeData.ModuleMatrix。
        //   行列读反会得到一张转置的二维码：三个定位角的位置看起来完全正常，但扫不出来，
        //   肉眼几乎不可能发现。P0 探针那边也记过这个坑。
        //   纠错等级与探针保持一致（M），不要为了「更好看」降到 L。
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);

        var image = new BitmapImage();

        // SetSource 是同步的：返回时解码已完成，流可以立刻释放。
        // 若改用 SetSourceAsync，流必须活到 Task 完成，否则会拿到一张空白图。
        using var stream = new MemoryStream(png);
        image.SetSource(stream.AsRandomAccessStream());

        return image;
    }
}
