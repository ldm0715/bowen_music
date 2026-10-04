using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 曲目的统计入口：图标 + 数量角标，可绑定命令。
/// </summary>
/// <remarks>
/// 图标有「未激活 / 已激活」两态：<see cref="Data"/> 与 <see cref="ActiveData"/>，由
/// <see cref="IsActive"/> 切换。分享按钮不分状态，不传 <see cref="ActiveData"/> 即可。
/// 两者都是**路径文本**而不是 <c>Geometry</c>，理由见 <see cref="IconGeometry"/>。
/// </remarks>
public sealed partial class TrackStatisticButton : UserControl
{
    public TrackStatisticButton() => InitializeComponent();

    private static void OnDisplayChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((TrackStatisticButton)sender).Bindings.Update();

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(string), typeof(TrackStatisticButton), new PropertyMetadata("", OnDisplayChanged));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(TrackStatisticButton), new PropertyMetadata("", OnDisplayChanged));
    public static readonly DependencyProperty BadgeTextProperty = DependencyProperty.Register(
        nameof(BadgeText), typeof(string), typeof(TrackStatisticButton), new PropertyMetadata("", OnDisplayChanged));
    public static readonly DependencyProperty HasBadgeCountProperty = DependencyProperty.Register(
        nameof(HasBadgeCount), typeof(bool), typeof(TrackStatisticButton), new PropertyMetadata(false, OnDisplayChanged));

    public static readonly DependencyProperty ActiveDataProperty = DependencyProperty.Register(
        nameof(ActiveData), typeof(string), typeof(TrackStatisticButton), new PropertyMetadata("", OnDisplayChanged));
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(TrackStatisticButton), new PropertyMetadata(false, OnDisplayChanged));
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(TrackStatisticButton), new PropertyMetadata(null, OnDisplayChanged));
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(TrackStatisticButton), new PropertyMetadata(null, OnDisplayChanged));

    /// <summary>未激活时的图标路径文本：<c>Data="{StaticResource IconHeart}"</c>。</summary>
    public string Data { get => (string)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string BadgeText { get => (string)GetValue(BadgeTextProperty); set => SetValue(BadgeTextProperty, value); }
    public bool HasBadgeCount { get => (bool)GetValue(HasBadgeCountProperty); set => SetValue(HasBadgeCountProperty, value); }

    /// <summary>已激活时的图标路径文本；与 <see cref="Data"/> 相同则两态长得一样。</summary>
    public string ActiveData { get => (string)GetValue(ActiveDataProperty); set => SetValue(ActiveDataProperty, value); }

    /// <summary>已激活状态。喜欢按钮绑的是「是否已喜欢」。</summary>
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    /// <summary>
    /// 内部按钮的点按事件。走命令的用法不需要它；评论入口没有命令，靠它接页面处理器。
    /// </summary>
    public event RoutedEventHandler? Click;

    /// <summary>把键盘焦点交给内部按钮，供收起浮层后归还焦点。</summary>
    public bool FocusButton(FocusState state) => InnerButton.Focus(state);

    private void OnInnerClick(object sender, RoutedEventArgs args) => Click?.Invoke(this, args);
}
