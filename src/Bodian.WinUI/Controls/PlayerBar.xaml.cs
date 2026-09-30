using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Bodian.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Bodian.WinUI.Controls;

/// <summary>底部播放条。</summary>
public sealed partial class PlayerBar : UserControl
{
    private readonly INavigationService _navigation;

    public PlayerBar(PlayerViewModel viewModel, LyricsViewModel lyrics, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(navigation);

        ViewModel = viewModel;
        Lyrics = lyrics;
        _navigation = navigation;

        InitializeComponent();

        // Slider 内部会把指针事件标记为已处理，所以要用 handledEventsToo: true 才收得到。
        // 拖动期间不 seek、松开才 seek，避免连续拖拽时把播放位置抖来抖去。
        PositionSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSliderPressed), true);
        PositionSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSliderReleased), true);
    }

    public PlayerViewModel ViewModel { get; }

    /// <summary>歌词页是否活跃。播放条上的「词」按钮按它置灰。</summary>
    public LyricsViewModel Lyrics { get; }

    private void OnSliderPressed(object sender, PointerRoutedEventArgs e) => ViewModel.IsSeeking = true;

    private void OnSliderReleased(object sender, PointerRoutedEventArgs e) =>
        _ = ViewModel.SeekToAsync(PositionSlider.Value);

    /// <summary>进歌词页。已经在歌词页时导航服务会直接忽略，不会把页面叠起来。</summary>
    private void OnLyricsClick(object sender, RoutedEventArgs e) => _navigation.Navigate<LyricsPage>();
}
