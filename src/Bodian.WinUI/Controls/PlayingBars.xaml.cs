using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Shapes;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 「正在播放」的三根起伏条。
/// </summary>
/// <remarks>
/// <para>
/// <b>只迁了设计参数</b>（条宽 3、圆角、间距 3、1 秒周期、0 / 0.2 / 0.4 秒的相位差、
/// 关键帧高度 4 → 14 → 6 → 12 → 4），代码是独立重写的 ——
/// 参照物 <c>LyciaMusic</c> 是 AGPL-3.0，本项目是 GPL-3.0，只可迁参数不可抄源码，
/// 边界见 <c>docs/ui-refresh.md</c> §0，这一处的记录见同文档 §17。
/// </para>
/// <para>
/// <b>用 Composition 的 ScaleY，不是改 Height。</b> 改高度每帧都要重新布局，
/// 而且只能当 dependent animation 跑在 UI 线程上 —— 一个列表里同时有好几行在播，
/// 那是拿布局换一个纯装饰效果。缩放是独立动画，跑在合成器线程上，不进布局。
/// 所以三根的 <c>Height</c> 固定为峰值 14，动画在 <c>4/14</c> 与 <c>1</c> 之间缩放。
/// </para>
/// <para>
/// <b>锚点在正中</b>（<c>CenterPoint = (1.5, 7)</c>）：参照物那三根是 flex 居中的，
/// 高度变化时上下对称地长，不是从底部长起来。
/// </para>
/// <para>
/// <b>显隐也由 <see cref="IsPlaying"/> 管</b>，行模板不要再另外绑 <c>Visibility</c>：
/// 绑两处的话，「先折叠还是先停动画」就取决于两个绑定回调的先后顺序，
/// 而这里需要的是先让它进树、再开动画。
/// </para>
/// </remarks>
public sealed partial class PlayingBars : UserControl
{
    /// <summary>条宽与峰值高度，与 XAML 里那三根一致。</summary>
    private const float BarWidth = 3;

    private const float BarHeight = 14;

    /// <summary>一个循环 1 秒，三根分别错开 0 / 0.2 / 0.4 秒。</summary>
    private static readonly TimeSpan Cycle = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan[] Phases =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(0.2),
        TimeSpan.FromSeconds(0.4),
    ];

    /// <summary>关键帧：归一化时间与缩放倍数（高度换算过来的）。</summary>
    private static readonly (float Time, float Scale)[] Keyframes =
    [
        (0f, 4f / BarHeight),
        (0.25f, 1f),
        (0.5f, 6f / BarHeight),
        (0.75f, 12f / BarHeight),
        (1f, 4f / BarHeight),
    ];

    private readonly Rectangle[] _bars;

    /// <summary>动画是否正在跑。<b>停止时据此早退</b>，见 <see cref="Stop"/>。</summary>
    private bool _running;

    public PlayingBars()
    {
        InitializeComponent();

        _bars = [Bar1, Bar2, Bar3];

        // ★ 只挂 Loaded，**不挂 Unloaded**。容器回收给别的行时 IsPlaying 会跟着换值，
        //   Apply 自然把动画停掉；而整页离开时视觉树连同它的视觉一起释放，
        //   不需要谁来收尾 —— 反倒是在 Unloaded 里再去取元素的视觉是有风险的，
        //   那个元素正在离开树。
        Loaded += (_, _) => Apply();
    }

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying),
        typeof(bool),
        typeof(PlayingBars),
        new PropertyMetadata(false, (sender, _) => ((PlayingBars)sender).Apply()));

    /// <summary>这一行是不是正在播放。为真时显示并开始起伏，为假时停住并折叠。</summary>
    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    private void Apply()
    {
        Visibility = IsPlaying ? Visibility.Visible : Visibility.Collapsed;

        if (IsPlaying && IsLoaded)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private void Start()
    {
        for (var i = 0; i < _bars.Length; i++)
        {
            var visual = ElementCompositionPreview.GetElementVisual(_bars[i]);

            // 锚点取正中：条高变化时上下对称地长，与参照物的 flex 居中一致。
            visual.CenterPoint = new Vector3(BarWidth / 2f, BarHeight / 2f, 0);

            // 相位的头 0.2 / 0.4 秒里动画还没开始，先把基础值摆成起始高度，
            // 否则那两根会先以满高亮相、再突然跳到 4px 重新长。
            visual.Scale = new Vector3(1f, Keyframes[0].Scale, 1f);

            using var easing = visual.Compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.42f, 0), new Vector2(0.58f, 1));
            using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();

            foreach (var (time, scale) in Keyframes)
            {
                animation.InsertKeyFrame(time, scale, easing);
            }

            animation.Duration = Cycle;
            animation.DelayTime = Phases[i];
            animation.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Scale.Y", animation);
        }

        _running = true;
    }

    private void Stop()
    {
        // ★ 没在动就一个合成器调用都不发。绝大多数行从来不是「正在播放」，
        //   它们每次进可视树都会走到这里，没必要为它们碰一次合成器。
        if (!_running)
        {
            return;
        }

        _running = false;

        foreach (var bar in _bars)
        {
            // ★ 必须是 StopAnimation，**不能写 StartAnimation("Scale.Y", null)**。
            //   文档说传 null 等于「停止并还原成基础值」，但 WinUI 的 ABI 层不接空的
            //   CompositionAnimation：实测直接访问违例（0xc0000005）闪退，
            //   而且因为每次 Loaded 都会调到这里，表现是「一进曲目列表就崩」。
            ElementCompositionPreview.GetElementVisual(bar).StopAnimation("Scale.Y");
        }
    }
}
