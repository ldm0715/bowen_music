using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace Bodian.WinUI.Services;

/// <summary>统一的合成动画；同一元素的新动画会终止旧动画，卸载时恢复视觉状态。</summary>
internal static class AppMotion
{
    private static readonly UISettings Settings = new();
    private static readonly ConditionalWeakTable<FrameworkElement, MotionState> States = new();
    public static bool IsEnabled => Settings.AnimationsEnabled;
    public static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Standard = TimeSpan.FromMilliseconds(260);
    public static readonly TimeSpan Expand = TimeSpan.FromMilliseconds(440);
    public static readonly TimeSpan Collapse = TimeSpan.FromMilliseconds(340);

    private sealed class MotionState
    {
        public Action? Cancel;
    }

    private static MotionState State(FrameworkElement element) => States.GetValue(element, target =>
    {
        target.Unloaded += OnUnloaded;
        return new MotionState();
    });

    private static void OnUnloaded(object sender, RoutedEventArgs args) => Reset((FrameworkElement)sender);

    public static void Reset(FrameworkElement element)
    {
        var state = State(element);
        state.Cancel?.Invoke();
        state.Cancel = null;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Translation");
        visual.StopAnimation("Scale");
        visual.StopAnimation("Opacity");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.Scale = Vector3.One;
        visual.Opacity = 1;
    }

    public static Task<bool> EnterAsync(FrameworkElement element, float x = 0, float y = 18,
        TimeSpan? duration = null, float scale = 1, bool bottomOrigin = false)
        => PlayAsync(element, new Vector3(x, y, 0), Vector3.Zero, 0, 1,
            new Vector3(scale, scale, 1), Vector3.One, duration ?? Standard, bottomOrigin: bottomOrigin);

    public static Task<bool> ExitAsync(FrameworkElement element, float x = 0, float y = 0,
        TimeSpan? duration = null, float scale = 1, bool bottomOrigin = false)
        => PlayAsync(element, Vector3.Zero, new Vector3(x, y, 0), 1, 0,
            Vector3.One, new Vector3(scale, scale, 1), duration ?? Quick,
            accelerating: true, bottomOrigin: bottomOrigin);

    public static async Task SwapAsync(FrameworkElement element, Action replace, int direction)
    {
        if (!element.IsLoaded || !IsEnabled)
        {
            Reset(element);
            replace();
            return;
        }
        // 先淡出旧内容再换数据；下一次请求或布局变化会让旧请求返回 false。
        if (!await ExitAsync(element, -direction * 38, 0)) return;
        replace();
        await EnterAsync(element, direction * 56, 0);
    }

    public static Task<bool> PlayAsync(FrameworkElement element, Vector3 from, Vector3 to,
        float fromOpacity, float toOpacity, Vector3 fromScale, Vector3 toScale, TimeSpan duration,
        bool accelerating = false, bool bottomOrigin = false, bool topLeftOrigin = false)
    {
        Reset(element);
        if (!IsEnabled)
            return Task.FromResult(true);

        var state = State(element);
        var result = new TaskCompletionSource<bool>();
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.Properties.InsertVector3("Translation", from);
        visual.Scale = fromScale;
        visual.Opacity = fromOpacity;
        RoutedEventHandler? loaded = null;

        void Start()
        {
            if (loaded is not null) element.Loaded -= loaded;
            visual.CenterPoint = topLeftOrigin ? Vector3.Zero : new Vector3(
                (float)element.ActualWidth / 2,
                (float)element.ActualHeight * (bottomOrigin ? 1 : 0.5f), 0);
            var compositor = visual.Compositor;
            using var easing = compositor.CreateCubicBezierEasingFunction(
                accelerating ? new Vector2(0.55f, 0) : new Vector2(0.16f, 1),
                accelerating ? new Vector2(0.9f, 0.55f) : new Vector2(0.3f, 1));
            using var translation = compositor.CreateVector3KeyFrameAnimation();
            translation.InsertKeyFrame(0, from);
            translation.InsertKeyFrame(1, to, easing);
            translation.Duration = duration;
            using var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(0, fromOpacity);
            if (accelerating) opacity.InsertKeyFrame(0.6f, fromOpacity * 0.85f);
            opacity.InsertKeyFrame(1, toOpacity, easing);
            opacity.Duration = duration;
            using var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(0, fromScale);
            scale.InsertKeyFrame(1, toScale, easing);
            scale.Duration = duration;
            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            Windows.Foundation.TypedEventHandler<object, CompositionBatchCompletedEventArgs>? completed = null;

            void Finish(bool finished)
            {
                batch.Completed -= completed;
                batch.Dispose();
                state.Cancel = null;
                result.TrySetResult(finished);
            }
            completed = (_, _) => Finish(true);
            state.Cancel = () => Finish(false);
            batch.Completed += completed;
            // 把终值作为底值，动画正常结束后不会回弹到起点。
            visual.Properties.InsertVector3("Translation", to);
            visual.Opacity = toOpacity;
            visual.Scale = toScale;
            visual.StartAnimation("Translation", translation);
            visual.StartAnimation("Opacity", opacity);
            visual.StartAnimation("Scale", scale);
            batch.End();
        }

        if (element.IsLoaded) Start();
        else
        {
            loaded = (_, _) => Start();
            state.Cancel = () =>
            {
                element.Loaded -= loaded;
                state.Cancel = null;
                result.TrySetResult(false);
            };
            element.Loaded += loaded;
        }
        return result.Task;
    }
}
