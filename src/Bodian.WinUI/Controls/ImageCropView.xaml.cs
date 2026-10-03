using System.Runtime.InteropServices.WindowsRuntime;
using Bodian.Core.Media;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 封面裁剪器：固定 1.25:1 的视口里拖动/缩放图片，按视口裁出一张 JPEG。
/// </summary>
/// <remarks>
/// <para>
/// <b>比例 1.25、最长边 1400 来自官方客户端</b>（<c>edit_user_playlist.dart:2645-2659</c>：
/// <c>maxWidth = maxHeight = 1400</c>、<c>chipRatio = 1.25</c>）。沿用同一套参数，
/// 我们裁出来的构图才和官方一致。
/// </para>
/// <para>
/// <b>视口比例由 code-behind 精确算出来</b>，不用 <c>Viewbox</c> 之类的布局手段 ——
/// 差一点点，裁出来的就不是那个构图。
/// </para>
/// <para>
/// <b>缩放平移复用 Core 的 <see cref="ImageViewportState"/></b>（与评论看图同一个状态机，
/// 那个是可单测的），打开 <see cref="ImageViewportState.CoverMode"/> 才有「铺满、可裁」的语义。
/// 视口 → 源图像素的换算也在那个类里（<see cref="ImageViewportState.SourceRect"/>）。
/// </para>
/// <para>
/// <b>出图走 Win2D 而不是 <c>RenderTargetBitmap</c></b>：后者只能拿到显示分辨率的像素，
/// 放大过后会糊。<b>也不用 <c>BitmapTransform.Bounds</c></b> —— 那个矩形与 EXIF 方向的
/// 先后关系不好确定，而 Win2D 的 <c>DrawImage</c> 源矩形作用在已经摆正的位图上，
/// 不存在这层歧义。
/// </para>
/// </remarks>
public sealed partial class ImageCropView : UserControl
{
    /// <summary>裁剪框的宽高比。官方客户端用的就是这个值。</summary>
    public const double AspectRatio = 1.25;

    /// <summary>输出图片最长边的上限。官方客户端传的 <c>maxWidth/maxHeight</c>。</summary>
    public const int MaxOutputEdge = 1400;

    /// <summary>裁剪视口的显示高度（DIP）。宽度 = 高度 × <see cref="AspectRatio"/>。</summary>
    /// <remarks>
    /// 定死而不是按容器算：见 XAML 里的说明 —— 量回来的写法有两种情形都会让视口变成 0×0，
    /// 而 0 尺寸的子树在渲染时会被整个丢掉。
    /// </remarks>
    private const double ViewportHeight = 280;

    /// <summary>JPEG 质量。官方没给值，取一个肉眼无损又不至于太大的。</summary>
    private const float JpegQuality = 0.9f;

    private readonly ImageViewportState _viewport = new() { CoverMode = true };
    private readonly InputSystemCursor _panCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);

    private SoftwareBitmap? _bitmap;
    private uint? _panPointerId;
    private Point _panStart;
    private double _panStartX;
    private double _panStartY;

    public ImageCropView()
    {
        InitializeComponent();

        Loaded += (_, _) => LayoutViewport();
        Unloaded += (_, _) => EndPan();
    }

    /// <summary>图显示不出来。宿主据此提示用户，而不是留一片空白。</summary>
    public event EventHandler? PreviewFailed;

    /// <summary>图片装载好了没有。没装载时 <see cref="RenderAsync"/> 给不出东西。</summary>
    public bool HasImage => _bitmap is { PixelWidth: > 0, PixelHeight: > 0 };

    /// <summary>
    /// 装载一张待裁剪的图。返回 <c>false</c> 表示这个文件解不开（格式不支持或已损坏）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>EXIF 方向在这里一次性摆正</b>（<c>RespectExifOrientation</c>），之后预览与出图
    /// 用的是同一张已摆正的位图 —— 手机竖拍的照片不会躺倒，也不会出现「预览是正的、
    /// 裁出来是歪的」。顺带统一成 BGRA8 + sRGB，出图不必再管源格式。
    /// </para>
    /// <para>
    /// <b>预览显示走 <see cref="BitmapImage"/>，不用 <c>SoftwareBitmapSource</c>。</b>
    /// 后者要么要求调用方一直持有位图、要么对格式挑剔，出问题时是**静默不显示**；
    /// 而本项目的图片一律走 <see cref="BitmapImage"/>（封面、二维码、评论看图都是）。
    /// 代价是把摆正后的位图再编码一次喂给它 —— 只影响预览，出图用的仍是原始位图，不损质量。
    /// </para>
    /// </remarks>
    public async Task<bool> LoadAsync(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        Clear();

        // 解码前先把视口尺寸交下去：Loaded 未必已经跑过（控件可能是刚切出来的）。
        LayoutViewport();

        if (bytes.Length == 0)
        {
            return false;
        }

        BusyRing.IsActive = true;

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());

            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);

            // ★ 必须是 Premultiplied：出图那步走的 Win2D 的
            //   CanvasBitmap.CreateFromSoftwareBitmap 只收 Premultiplied 的 Bgra8，
            //   换成 Ignore 会让「完成裁剪」直接抛 COMException。
            //   预览要的无 alpha 由 CreatePreviewAsync 单独转，两边各取所需。
            var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                bitmap.Dispose();
                return false;
            }

            var preview = await CreatePreviewAsync(bitmap);

            _bitmap = bitmap;

            // ★ 顺序要紧：先量出图片尺寸，再把它交给 Image。
            //   反过来（或漏掉 SetImageSize）会让 Fit 无从下手，图片宽度被算成 0 ——
            //   表现就是「导入了图，什么也看不见」。
            _viewport.SetImageSize(bitmap.PixelWidth, bitmap.PixelHeight);

            CropImage.Source = preview;
            FitToViewport();

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 解不开就是解不开（格式不支持、文件损坏）—— 由调用方决定怎么提示。
            Clear();
            return false;
        }
        finally
        {
            BusyRing.IsActive = false;
        }
    }

    /// <summary>把摆正后的位图编成一张可显示的图。<b>只用于预览</b>，出图不经过它。</summary>
    /// <remarks>
    /// <b>先转成无 alpha 再编码</b>：解码出来的位图带着 Premultiplied alpha（出图那步要），
    /// 而 <b>JPEG 编码器不收带 alpha 的位图</b> —— 直接喂进去拿到的是个坏流，
    /// 而 <c>BitmapImage</c> 拿到坏流**不抛异常**，只是什么都不画，是纯粹的静默失败。
    /// </remarks>
    private static async Task<BitmapImage> CreatePreviewAsync(SoftwareBitmap bitmap)
    {
        using var opaque = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        using var stream = new InMemoryRandomAccessStream();

        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        encoder.SetSoftwareBitmap(opaque);
        await encoder.FlushAsync();

        stream.Seek(0);

        var image = new BitmapImage();
        await image.SetSourceAsync(stream);

        return image;
    }

    /// <summary>
    /// 按当前视口裁出一张 JPEG。没装载图片或视口还没量出来时返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 尺寸按 <see cref="MaxOutputEdge"/> 收口：视口里的图放得再大，出图也不会超过 1400，
    /// 只会等比缩下来 —— 这既是对齐官方，也顺带压住了上传体积。
    /// </remarks>
    public async Task<byte[]?> RenderAsync()
    {
        if (_bitmap is not { } bitmap)
        {
            return null;
        }

        var (x, y, width, height) = _viewport.SourceRect();

        if (width < 1 || height < 1)
        {
            return null;
        }

        var scale = Math.Min(1, MaxOutputEdge / Math.Max(width, height));
        var outputWidth = Math.Max(1, (int)Math.Round(width * scale));
        var outputHeight = Math.Max(1, (int)Math.Round(height * scale));

        var device = CanvasDevice.GetSharedDevice();

        using var source = CanvasBitmap.CreateFromSoftwareBitmap(device, bitmap);
        using var target = new CanvasRenderTarget(device, outputWidth, outputHeight, 96);

        using (var session = target.CreateDrawingSession())
        {
            session.DrawImage(
                source,
                new Rect(0, 0, outputWidth, outputHeight),
                new Rect(x, y, width, height),
                1,
                CanvasImageInterpolation.HighQualityCubic);
        }

        using var buffer = new InMemoryRandomAccessStream();
        await target.SaveAsync(buffer, CanvasBitmapFileFormat.Jpeg, JpegQuality);

        buffer.Seek(0);
        var result = new byte[buffer.Size];

        using var reader = new DataReader(buffer.GetInputStreamAt(0));
        await reader.LoadAsync((uint)buffer.Size);
        reader.ReadBytes(result);

        return result;
    }

    private void Clear()
    {
        EndPan();
        CropImage.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _viewport.Reset();
        UpdateTransform();
    }

    // ── 比例与布局 ──────────────────────────────────────────────────────────

    /// <summary>把视口尺寸交给布局与状态机。尺寸是常量，见 XAML 里的说明。</summary>
    private void LayoutViewport()
    {
        var width = ViewportHeight * AspectRatio;

        CropViewport.Width = width;
        CropViewport.Height = ViewportHeight;
        CropViewportClip.Rect = new Rect(0, 0, width, ViewportHeight);

        _viewport.Resize(width, ViewportHeight);
        FitToViewport();
    }

    /// <summary>
    /// 按 cover 语义重置：铺满视口、居中。裁剪器**没有「缩小到看得见整张图」这一步** ——
    /// 那会露出视口外的空白，裁出来就不是一张完整的图。
    /// </summary>
    private void FitToViewport()
    {
        EndPan();
        _viewport.Fit();
        CropImage.Width = _viewport.FittedWidth;
        CropImage.Height = _viewport.FittedHeight;
        UpdateTransform();
    }

    // ── 手势 ────────────────────────────────────────────────────────────────

    private void OnImageOpened(object sender, RoutedEventArgs args) => FitToViewport();

    /// <summary>
    /// 图没显示出来。<b>不接这个事件的话是静默空白</b> —— <c>BitmapImage</c> 拿到坏流时
    /// 不抛异常，只让 <c>Image</c> 什么都不画。
    /// </summary>
    private void OnImageFailed(object sender, ExceptionRoutedEventArgs args) => PreviewFailed?.Invoke(this, EventArgs.Empty);

    private void OnViewportPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse || !_viewport.CanPan
            || !args.GetCurrentPoint(CropViewport).Properties.IsLeftButtonPressed || _panPointerId is not null)
        {
            return;
        }

        if (!CropViewport.CapturePointer(args.Pointer))
        {
            return;
        }

        _panStart = args.GetCurrentPoint(CropViewport).Position;
        _panStartX = _viewport.OffsetX;
        _panStartY = _viewport.OffsetY;
        _panPointerId = args.Pointer.PointerId;

        args.Handled = true;
    }

    private void OnViewportPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_panPointerId != args.Pointer.PointerId)
        {
            return;
        }

        var point = args.GetCurrentPoint(CropViewport);

        if (!point.Properties.IsLeftButtonPressed)
        {
            EndPan();
            return;
        }

        _viewport.PanTo(_panStartX + point.Position.X - _panStart.X, _panStartY + point.Position.Y - _panStart.Y);
        UpdateTransform();

        args.Handled = true;
    }

    private void OnViewportPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_panPointerId != args.Pointer.PointerId)
        {
            return;
        }

        EndPan();
        args.Handled = true;
    }

    private void OnViewportPointerCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        if (_panPointerId == args.Pointer.PointerId)
        {
            _panPointerId = null;
        }
    }

    private void OnViewportPointerWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        if (!HasImage)
        {
            return;
        }

        var point = args.GetCurrentPoint(CropViewport);
        SetZoom(_viewport.Zoom * Math.Pow(1.2, point.Properties.MouseWheelDelta / 120d), point.Position);

        args.Handled = true;
    }

    private void OnViewportManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs args)
    {
        if (!HasImage)
        {
            return;
        }

        _viewport.PanBy(args.Delta.Translation.X, args.Delta.Translation.Y);

        if (Math.Abs(args.Delta.Scale - 1) > 0.0001)
        {
            SetZoom(_viewport.Zoom * args.Delta.Scale, args.Position);
        }

        UpdateTransform();
        args.Handled = true;
    }

    /// <summary>
    /// 缩放。<b>下限钉死在 1</b>：cover 语义下缩小就会露出空白，那不是用户想裁的东西。
    /// </summary>
    private void SetZoom(double factor, Point anchor)
    {
        EndPan();
        _viewport.ZoomTo(Math.Max(1, factor), anchor.X, anchor.Y);
        UpdateTransform();
    }

    private void EndPan()
    {
        var wasPanning = _panPointerId is not null;
        _panPointerId = null;

        if (wasPanning)
        {
            CropViewport.ReleasePointerCaptures();
        }
    }

    /// <summary>
    /// 把视口状态落到图上的变换：先缩放、再平移，原点在左上角。
    /// 与 <see cref="ImageViewportState.SourceRect"/> 用的是同一组量，两边不会漂。
    /// </summary>
    private void UpdateTransform()
    {
        ProtectedCursor = _viewport.CanPan ? _panCursor : null;

        CropScale.ScaleX = _viewport.Zoom;
        CropScale.ScaleY = _viewport.Zoom;
        CropTranslate.X = _viewport.OffsetX;
        CropTranslate.Y = _viewport.OffsetY;
    }
}
