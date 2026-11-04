using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Bowen.Core.Models.Lyrics;
using Bowen.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Bowen.WinUI.Controls;

/// <summary>选择面板里的一句歌词。</summary>
public sealed partial class LyricPosterLineItem : ObservableObject
{
    public LyricPosterLineItem(LyricPosterLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Line = line;
    }

    public LyricPosterLine Line { get; }

    public string Text => Line.Text;

    public bool IsTranslation => Line.IsTranslation;

    /// <summary>译文行压暗一点，与原文区分；两边都是独立可选的选择项。</summary>
    public double DisplayOpacity => IsTranslation ? 0.65 : 1;

    /// <summary>译文行轻微缩进，视觉上仍能看出它属于上一句。</summary>
    public Thickness TextMargin => IsTranslation ? new Thickness(16, 0, 0, 0) : new Thickness(0);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>歌词海报的选句与预览界面。内容放在 <see cref="LyricPosterDialog"/> 的 ContentDialog 里。</summary>
public sealed partial class LyricPosterDialogContent : UserControl
{
    private readonly ObservableCollection<LyricPosterLineItem> _items = [];
    private readonly int _scrollIndex;
    private bool _scrolled;

    public LyricPosterDialogContent(LyricPosterDialogContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;

        InitializeComponent();

        TrackTitleText.Text = context.Track.Title;
        TrackArtistText.Text = context.Track.ArtistText;

        foreach (var line in context.Lines)
        {
            var item = new LyricPosterLineItem(line);
            item.PropertyChanged += OnItemPropertyChanged;
            _items.Add(item);
        }

        LineList.ItemsSource = _items;

        // 打开时滚到当前播放行，但不勾选：选哪些由用户自己决定。
        _scrollIndex = context.DefaultLineIndex >= 0 && context.DefaultLineIndex < _items.Count
            ? context.DefaultLineIndex
            : _items.Count > 0 ? 0 : -1;
        LineList.Loaded += OnLineListLoaded;

        if (_items.Count == 0)
        {
            LineList.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Visible;
            SelectAllButton.IsEnabled = false;
            ClearButton.IsEnabled = false;
        }

        UpdateSelectionState();
    }

    public LyricPosterDialogContext Context { get; }

    /// <summary>勾选集合变了。对话框据此决定「生成海报」是否可用。</summary>
    public event EventHandler? SelectionChanged;

    public int SelectedCount => _items.Count(item => item.IsSelected);

    /// <summary>按原歌词顺序返回已选句。</summary>
    public IReadOnlyList<LyricPosterLine> SelectedLines =>
        _items.Where(item => item.IsSelected).Select(item => item.Line).ToArray();

    /// <summary>在选句区显示一行提示（例如达到上限、生成失败）。</summary>
    public void ShowMessage(string message) => HintText.Text = message;

    public void SetBusy(bool busy)
    {
        PreviewProgress.IsActive = busy;
        PreviewProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        PreviewImage.Opacity = busy ? 0.3 : 1;
        PreviewMessage.Text = "";
    }

    /// <summary>用同一个渲染器生成 PNG，并把结果按比例显示出来。</summary>
    public async Task<byte[]> RenderPreviewAsync(LyricPosterContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var png = await LyricPosterRenderer.RenderAsync(content);

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);

        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);

        PreviewImage.Source = bitmap;
        return png;
    }

    public void ShowPreviewStage()
    {
        SelectionStage.Visibility = Visibility.Collapsed;
        PreviewStage.Visibility = Visibility.Visible;
    }

    public void ShowSelectionStage()
    {
        PreviewStage.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        PreviewMessage.Text = "";
        SelectionStage.Visibility = Visibility.Visible;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LyricPosterLineItem.IsSelected))
        {
            UpdateSelectionState();
        }
    }

    private void OnLineCheckChanged(object sender, RoutedEventArgs args) => UpdateSelectionState();

    /// <remarks>
    /// 等列表真正加载后再滚，否则 <see cref="ScrollIntoView"/> 会被随后的布局重置掉。
    /// </remarks>
    private void OnLineListLoaded(object sender, RoutedEventArgs args)
    {
        if (_scrolled || _scrollIndex < 0)
        {
            return;
        }

        _scrolled = true;
        DispatcherQueue.TryEnqueue(() =>
            LineList.ScrollIntoView(_items[_scrollIndex], ScrollIntoViewAlignment.Leading));
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs args)
    {
        foreach (var item in _items)
        {
            item.IsSelected = true;
        }

        HintText.Text = "";
    }

    private void OnClearClick(object sender, RoutedEventArgs args)
    {
        foreach (var item in _items)
        {
            item.IsSelected = false;
        }

        HintText.Text = "";
    }

    private void UpdateSelectionState()
    {
        CounterText.Text = $"已选 {SelectedCount} 行";
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
