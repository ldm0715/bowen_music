using System.ComponentModel;
using Bodian.Core.Media;
using Bodian.WinUI.Media;
using Bodian.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 氛围背景：三团缓慢漂移的柔和光晕，颜色取自当前封面。
/// </summary>
/// <remarks>
/// <para>
/// <b>常驻全屏层，所以性能是要点。</b> 三个 Storyboard 只驱动
/// <c>CompositeTransform</c> 的位移，走的是合成器线程的独立动画通道，
/// 不进 UI 线程 —— 空闲时 CPU 占用为 0。
/// </para>
/// <para>
/// <b>暂停/恢复是必须的。</b> <c>Loaded</c> 在控件重新挂载时会再次触发，
/// 不设防就会重复 <c>Begin</c>，表现为漂移速度翻倍。
/// </para>
/// <para>
/// <b>画刷全部在这里现造，不在 XAML 里留一份。</b> 运行期改已有画刷的
/// <c>GradientStops</c> <b>不一定能让它失效重绘</b> —— 实测的表现是三个矩形
/// 渲染成平色块、看不出是渐变。整个换掉 <c>Fill</c> 才稳妥，
/// 顺带把几何参数收到一处，不会和 XAML 里那份不同步。
/// </para>
/// <para>
/// <b>颜色的来源</b>：<c>Application.Current.Resources</c> 里的 <c>"BodianNowPlaying"</c>
/// 与 <c>"BodianLoggerFactory"</c>（由宿主放进 App 资源）。本控件是 XAML 实例化的，
/// 构造函数必须无参，拿不到 DI 容器 —— 这是唯一能拿到它们的通道。
/// </para>
/// </remarks>
public sealed partial class AmbientBackdrop : UserControl
{
    /// <summary>
    /// 三团的圆心与半径，都是相对窗口的比例。
    /// </summary>
    /// <remarks>
    /// <b>圆心要避开播放条</b>（占窗口底部约 y&gt;0.82，且自己有底色）：
    /// 圆心落在那儿等于整团被盖掉。所以最低的圆心放在 y=0.72。
    /// <para>
    /// 半径放到 0.9 以上是为了**全屏覆盖** —— 任何一个角落都要落在至少一个团的
    /// 有效范围内，否则边角会衰减到没有颜色，看起来像「没铺满」。
    /// </para>
    /// </remarks>
    private static readonly (double CenterX, double CenterY, double RadiusX, double RadiusY)[] BlobGeometry =
    [
        (0.20, 0.22, 0.95, 1.25),
        (0.80, 0.38, 0.95, 1.25),
        (0.48, 0.72, 0.90, 1.20),
    ];

    /// <summary>
    /// 每个色团三个色阶的偏移与透明度。
    /// </summary>
    /// <remarks>
    /// <b>最外那圈必须是 0（全透明）。</b> 不是 0 的话色团边界会显出一条硬边 ——
    /// 渐变到不了零，看起来就是一块有棱角的色斑。
    /// </remarks>
    private static readonly double[] StopOffsets = [0, 0.60, 1.0];

    private static readonly byte[] StopAlphas = [0x80, 0x47, 0x00];

    private readonly Rectangle[] _blobs;
    private readonly CoverPaletteLoader _loader;

    private bool _running;
    private PlayerViewModel? _player;

    public AmbientBackdrop()
    {
        InitializeComponent();

        _blobs = [BlobA, BlobB, BlobC];

        _loader = new CoverPaletteLoader(
            (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory)
                ?.CreateLogger<CoverPaletteLoader>());

        // 先把兜底配色铺上，避免开场那一下背景是空的。
        // 种子用中灰而不是 default（那是纯黑）—— 灰色会让 ColorPalette 退回品牌色相，
        // 而亮度取自种子，纯黑会得到一个偏暗的兜底色。
        ApplyPalette(ColorPalette.Ambient(new RgbColor(128, 128, 128)));

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            return;
        }

        _running = true;

        DriftA.Begin();
        DriftB.Begin();
        DriftC.Begin();

        HookPlayer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_running)
        {
            return;
        }

        _running = false;

        DriftA.Stop();
        DriftB.Stop();
        DriftC.Stop();

        if (_player is not null)
        {
            _player.PropertyChanged -= OnPlayerChanged;
            _player = null;
        }
    }

    private void HookPlayer()
    {
        if (_player is null)
        {
            if (Application.Current.Resources["BodianNowPlaying"] is PlayerViewModel player)
            {
                _player = player;
                _player.PropertyChanged += OnPlayerChanged;
            }
        }

        Refresh();
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.CurrentCoverUri) or nameof(PlayerViewModel.CurrentTrackId))
        {
            Refresh();
        }
    }

    /// <summary>
    /// 按当前封面重取颜色。
    /// </summary>
    /// <remarks>
    /// <b>取色是异步的，所以要挡竞态。</b> 连着切两首时，前一首的解码可能后完成，
    /// 结果是把旧歌的颜色刷上去。完成后再比对一次曲目 id，对不上就丢弃。
    /// </remarks>
    private async void Refresh()
    {
        if (_player is null)
        {
            return;
        }

        var trackId = _player.CurrentTrackId ?? 0;
        var palette = await _loader.LoadAsync(trackId, _player.CurrentCoverUri);

        if ((_player.CurrentTrackId ?? 0) != trackId)
        {
            // 取色期间又切歌了，这次的结果作废 —— 后一次调用会自己刷新。
            return;
        }

        ApplyPalette(palette);
    }

    private void ApplyPalette(IReadOnlyList<RgbColor> palette)
    {
        for (var i = 0; i < _blobs.Length; i++)
        {
            _blobs[i].Fill = BuildBrush(palette[i], BlobGeometry[i]);
        }
    }

    private static RadialGradientBrush BuildBrush(
        RgbColor color,
        (double CenterX, double CenterY, double RadiusX, double RadiusY) geometry)
    {
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new Point(geometry.CenterX, geometry.CenterY),
            RadiusX = geometry.RadiusX,
            RadiusY = geometry.RadiusY,
            SpreadMethod = GradientSpreadMethod.Pad,
        };

        for (var i = 0; i < StopOffsets.Length; i++)
        {
            brush.GradientStops.Add(new GradientStop
            {
                Offset = StopOffsets[i],
                Color = CoverPaletteLoader.ToColor(color, StopAlphas[i]),
            });
        }

        return brush;
    }
}
