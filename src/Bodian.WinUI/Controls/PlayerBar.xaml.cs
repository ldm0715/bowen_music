using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Bodian.WinUI.Controls;

/// <summary>底部播放条。</summary>
public sealed partial class PlayerBar : UserControl
{
    public PlayerBar(PlayerViewModel viewModel, LyricsViewModel lyrics)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(lyrics);

        ViewModel = viewModel;
        Lyrics = lyrics;

        InitializeComponent();

        // Slider 内部会把指针事件标记为已处理，所以要用 handledEventsToo: true 才收得到。
        // 拖动期间不 seek、松开才 seek，避免连续拖拽时把播放位置抖来抖去。
        PositionSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSliderPressed), true);
        PositionSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSliderReleased), true);
    }

    public PlayerViewModel ViewModel { get; }

    /// <summary>歌词面板。播放条上的「词」按钮归它管。</summary>
    public LyricsViewModel Lyrics { get; }

    private void OnSliderPressed(object sender, PointerRoutedEventArgs e) => ViewModel.IsSeeking = true;

    private void OnSliderReleased(object sender, PointerRoutedEventArgs e) =>
        _ = ViewModel.SeekToAsync(PositionSlider.Value);
}
