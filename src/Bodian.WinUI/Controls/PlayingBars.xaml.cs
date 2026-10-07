using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Bodian.WinUI.Controls;

/// <summary>当前曲目的三根播放标记；共享 20 Hz 时钟，暂停与不可见时完全停止。</summary>
public sealed partial class PlayingBars : UserControl
{
    private const float BarWidth = 3;
    private const float BarHeight = 14;
    private static readonly float[] Heights = [4, 14, 6, 12, 4];
    private static readonly HashSet<PlayingBars> ActiveInstances = [];
    private static readonly ConditionalWeakTable<XamlRoot, WindowRenderingState> WindowStates = new();
    private static DispatcherQueueTimer? _timer;
    private static PlayerViewModel? _player;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private readonly Rectangle[] _bars;
    private readonly List<Visual> _visuals = [];
    private bool _running;

    public PlayingBars()
    {
        InitializeComponent();
        _bars = [Bar1, Bar2, Bar3];
        Loaded += (_, _) => Apply();
        Unloaded += (_, _) =>
        {
            ActiveInstances.Remove(this);
            _running = false;
            _visuals.Clear();
            UpdateTimer();
        };
    }

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying), typeof(bool), typeof(PlayingBars),
        new PropertyMetadata(false, (sender, _) => ((PlayingBars)sender).Apply()));

    /// <summary>此行是否为当前曲目；暂停时保留静态标记。</summary>
    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    private void Apply()
    {
        Visibility = IsPlaying ? Visibility.Visible : Visibility.Collapsed;
        if (IsPlaying && IsLoaded) ActiveInstances.Add(this);
        else ActiveInstances.Remove(this);
        if (_player is null && Application.Current.Resources["BodianNowPlaying"] is PlayerViewModel player)
        {
            _player = player;
            player.PropertyChanged += OnPlaybackChanged;
        }
        UpdateAnimation();
        UpdateTimer();
    }

    internal static void SetWindowRendering(XamlRoot? root, bool enabled)
    {
        if (root is null) return;
        WindowStates.GetValue(root, static _ => new WindowRenderingState()).Enabled = enabled;
        foreach (var instance in ActiveInstances)
            if (ReferenceEquals(instance.XamlRoot, root)) instance.UpdateAnimation();
        UpdateTimer();
    }

    private static void OnPlaybackChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(PlayerViewModel.IsPlaying)) return;
        foreach (var instance in ActiveInstances) instance.UpdateAnimation();
        UpdateTimer();
    }

    private void UpdateAnimation()
    {
        var visible = XamlRoot is null || !WindowStates.TryGetValue(XamlRoot, out var state) || state.Enabled;
        _running = visible && IsPlaying && IsLoaded && HasVisibleAncestors() && _player?.IsPlaying == true;
        if (!IsPlaying || !IsLoaded) return;
        if (_visuals.Count == 0)
            foreach (var bar in _bars)
            {
                var visual = ElementCompositionPreview.GetElementVisual(bar);
                visual.CenterPoint = new Vector3(BarWidth / 2, BarHeight / 2, 0);
                visual.Scale = new Vector3(1, Heights[0] / BarHeight, 1);
                _visuals.Add(visual);
            }
        if (!_running)
            foreach (var visual in _visuals) visual.Scale = new Vector3(1, Heights[0] / BarHeight, 1);
    }

    private static void UpdateTimer()
    {
        if (!ActiveInstances.Any(instance => instance._running)) { _timer?.Stop(); return; }
        if (_timer is null)
        {
            _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(50);
            _timer.Tick += (_, _) =>
            {
                var seconds = Clock.Elapsed.TotalSeconds;
                foreach (var instance in ActiveInstances)
                    if (instance._running) instance.UpdateBars(seconds);
            };
        }
        if (!_timer.IsRunning) _timer.Start();
    }

    private void UpdateBars(double seconds)
    {
        for (var index = 0; index < _visuals.Count; index++)
        {
            var phase = ((seconds - index * 0.2) % 1 + 1) % 1 * 4;
            var segment = Math.Min(3, (int)phase);
            var fraction = phase - segment;
            fraction = fraction * fraction * (3 - 2 * fraction);
            var scale = (float)((Heights[segment] + (Heights[segment + 1] - Heights[segment]) * fraction) / BarHeight);
            _visuals[index].Scale = new Vector3(1, scale, 1);
        }
    }

    private bool HasVisibleAncestors()
    {
        for (DependencyObject? element = this; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }

    private sealed class WindowRenderingState
    {
        public bool Enabled { get; set; } = true;
    }
}
