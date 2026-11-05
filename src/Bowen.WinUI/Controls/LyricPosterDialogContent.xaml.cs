using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Bowen.Core.Media;
using Bowen.Core.Models;
using Bowen.Core.Models.Lyrics;
using Bowen.WinUI.Media;
using Bowen.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

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

/// <summary>
/// 歌词海报的选句、预览与自定义界面。
/// </summary>
/// <remarks>
/// <b>预览和导出共用同一个渲染器。</b> 任何设置变化都会重新渲染；渲染用信号量串行化，
/// 迟到的旧结果通过版本号丢弃，保存 / 复制只认 <see cref="LatestPng"/>。
/// </remarks>
public sealed partial class LyricPosterDialogContent : UserControl
{
    private static readonly Color AccentColor = Color.FromArgb(0xFF, 0x00, 0xD9, 0xA0);
    private static readonly Color TransparentColor = Color.FromArgb(0x00, 0x00, 0x00, 0x00);

    private readonly ObservableCollection<LyricPosterLineItem> _items = [];
    private readonly int _scrollIndex;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly List<(Button Button, LyricPosterBackgroundSource Source, uint Argb)> _backgroundSwatches = [];
    private readonly List<(Button Button, LyricPosterTextColorMode Mode, uint Argb, bool IsCustom)> _textSwatches = [];
    private readonly SolidColorBrush _accentBrush = new(AccentColor);
    private readonly SolidColorBrush _clearBrush = new(TransparentColor);

    private LyricPosterSettings _settings;
    private Task<RgbColor>? _autoBackgroundTask;
    private Border? _autoBackgroundDot;
    private Border? _autoTextDot;
    private Border? _backgroundCustomDot;
    private Border? _textCustomDot;
    private bool _scrolled;
    private bool _initializing = true;
    private bool _renderSuspended;
    private int _renderVersion;

    public LyricPosterDialogContent(LyricPosterDialogContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        _settings = context.SettingsStore.Load().Normalized();

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

        BuildSettingsControls();
        UpdateSelectionState();
        UpdateSwatchSelection();
        UpdateContrastHint();
        _ = InitializeAutoSwatchAsync();

        Unloaded += (_, _) => SuspendRender();
    }

    public LyricPosterDialogContext Context { get; }

    /// <summary>勾选集合变了。对话框据此决定「生成海报」是否可用。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>开始 / 结束渲染。宿主据此禁用保存、复制。</summary>
    public event EventHandler? RenderStateChanged;

    /// <summary>点标题栏的关闭按钮；由宿主关闭对话框。</summary>
    public event EventHandler? CloseRequested;

    public int SelectedCount => _items.Count(item => item.IsSelected);

    /// <summary>按原歌词顺序返回已选句。</summary>
    public IReadOnlyList<LyricPosterLine> SelectedLines =>
        _items.Where(item => item.IsSelected).Select(item => item.Line).ToArray();

    /// <summary>最近一次成功渲染的 PNG。保存 / 复制必须用它。</summary>
    public byte[]? LatestPng { get; private set; }

    public bool IsRendering { get; private set; }

    public string? LastRenderError { get; private set; }

    /// <summary>在选句区显示一行提示（例如达到上限、生成失败）。</summary>
    public void ShowMessage(string message) => HintText.Text = message;

    /// <summary>
    /// 复制所选歌词文本；没有勾选时不复制。
    /// </summary>
    /// <remarks>选句阶段的底部按钮用它；关闭对话框走标题栏的 X。</remarks>
    public void CopyLyrics()
    {
        var selected = SelectedLines;

        if (selected.Count == 0)
        {
            HintText.Text = "先选择歌词。";
            return;
        }

        try
        {
            Context.Clipboard.SetText(string.Join(Environment.NewLine, selected.Select(line => line.Text)));
            HintText.Text = $"已复制所选 {selected.Count} 行歌词";
        }
        catch (Exception exception)
        {
            Context.Logger?.LogWarning(exception, "复制海报歌词失败");
            HintText.Text = "复制歌词失败，请稍后再试。";
        }
    }

    /// <summary>
    /// 重新渲染当前选择与设置。
    /// </summary>
    /// <remarks>
    /// 用信号量串行化：排队的请求拿到锁后先看版本号，已经过期的直接返回；
    /// 最新那个版本负责更新预览与 <see cref="LatestPng"/>。
    /// </remarks>
    public async Task RenderLatestAsync()
    {
        _renderSuspended = false;
        var version = ++_renderVersion;

        await _renderGate.WaitAsync();
        try
        {
            if (version != _renderVersion || _renderSuspended)
            {
                return;
            }

            SetRendering(true);
            LastRenderError = null;

            // 新一轮渲染开始就作废旧图：两次渲染之间即使按钮短暂恢复，也不能保存旧样式。
            LatestPng = null;

            try
            {
                var style = await ResolveStyleAsync();
                var png = await LyricPosterRenderer.RenderAsync(new LyricPosterContent(
                    Context.Track,
                    Context.ShareLink,
                    SelectedLines,
                    style));

                if (version != _renderVersion || _renderSuspended)
                {
                    return;
                }

                await SetPreviewBitmapAsync(png);

                if (version != _renderVersion || _renderSuspended)
                {
                    return;
                }

                LatestPng = png;
                PreviewMessage.Text = "";
                HintText.Text = "";
            }
            catch (LyricPosterOverflowException)
            {
                LatestPng = null;
                LastRenderError = "当前字体下歌词放不下，请减少勾选或换一个字体。";
                ShowRenderError(LastRenderError);
            }
            catch (Exception exception)
            {
                LatestPng = null;
                LastRenderError = "海报生成失败，请稍后再试。";
                ShowRenderError(LastRenderError);
                Context.Logger?.LogWarning(exception, "生成歌词海报失败");
            }
            finally
            {
                SetRendering(false);
            }
        }
        finally
        {
            _renderGate.Release();
        }
    }

    public void ShowPreviewStage()
    {
        // 预览阶段右侧多一列设置，选择阶段保持原来的窄宽度。
        Width = 560;
        Height = double.NaN;
        SelectionStage.Visibility = Visibility.Collapsed;
        PreviewStage.Visibility = Visibility.Visible;
    }

    public void ShowSelectionStage()
    {
        SuspendRender();
        PosterViewer.Visibility = Visibility.Collapsed;
        PosterViewer.ImageSource = null;
        Width = 520;
        Height = double.NaN;
        PreviewStage.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        PreviewMessage.Text = "";
        SelectionStage.Visibility = Visibility.Visible;
    }

    private void SuspendRender()
    {
        _renderSuspended = true;
        _renderVersion++;
        LatestPng = null;
    }

    private void SetRendering(bool rendering)
    {
        IsRendering = rendering;
        PreviewProgress.IsActive = rendering;
        PreviewProgress.Visibility = rendering ? Visibility.Visible : Visibility.Collapsed;

        if (rendering && PreviewImage.Source is null)
        {
            PreviewImage.Opacity = 0.3;
        }
        else if (!rendering)
        {
            PreviewImage.Opacity = 1;
        }

        RenderStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowRenderError(string message)
    {
        PreviewMessage.Text = message;
        HintText.Text = message;
    }

    private async Task SetPreviewBitmapAsync(byte[] png)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);

        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        PreviewImage.Source = bitmap;

        // 看大图开着时换图：同一张图直接换源，保持缩放视图不跳。
        if (PosterViewer.Visibility == Visibility.Visible)
        {
            PosterViewer.ImageSource = bitmap;
        }
    }

    private void OnCloseDialogClick(object sender, RoutedEventArgs args)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnPreviewTapped(object sender, TappedRoutedEventArgs args)
    {
        if (PreviewImage.Source is not ImageSource source)
        {
            return;
        }

        // 按海报自身的宽高比算查看器尺寸：竖版海报就得到一个竖版查看器，
        // 只用窗口高度把它撑大，不再拉成满窗宽。
        var screen = XamlRoot?.Size ?? new Windows.Foundation.Size(900, 700);
        var aspect = source is BitmapImage { PixelHeight: > 0 } bitmap
            ? bitmap.PixelWidth / (double)bitmap.PixelHeight
            : 0.6;
        aspect = Math.Clamp(aspect, 0.35, 1.2);

        var contentHeight = Math.Max(420, screen.Height - 160);
        var viewportHeight = Math.Max(300, contentHeight - 152);
        var contentWidth = Math.Max(360, (viewportHeight * aspect) + 64);

        Width = Math.Min(screen.Width - 80, contentWidth);
        Height = contentHeight;

        PosterViewer.Title = "歌词海报";
        PosterViewer.ImageSource = source;
        PosterViewer.Visibility = Visibility.Visible;
        DispatcherQueue.TryEnqueue(PosterViewer.FocusCloseButton);
    }

    private void OnPosterViewerClose(object? sender, EventArgs args)
    {
        PosterViewer.Visibility = Visibility.Collapsed;
        PosterViewer.ImageSource = null;
        Width = 560;
        Height = double.NaN;
    }

    // ── 设置 ───────────────────────────────────────────────────────────────

    private void BuildSettingsControls()
    {
        PosterFontPicker.ItemsSource = Context.FontOptions;
        PosterFontPicker.StretchToAvailableWidth();
        PosterFontPicker.SelectedIndex = FindFontIndex(_settings.FontFamily);
        PosterFontPicker.RegisterPropertyChangedCallback(FontPicker.SelectedIndexProperty, OnFontPickerChanged);

        BuildBackgroundSwatches();
        BuildTextSwatches();
        _initializing = false;
    }

    private void BuildBackgroundSwatches()
    {
        var cells = new List<FrameworkElement>();

        _autoBackgroundDot = CreateDot(new SolidColorBrush(ToColor(ToRgb(LyricPosterSettings.DefaultBackgroundColor))));
        var auto = CreateSwatchButton(_autoBackgroundDot, "跟随封面自动取色",
            () => ApplySettings(_settings with { BackgroundSource = LyricPosterBackgroundSource.Auto }));
        _backgroundSwatches.Add((auto, LyricPosterBackgroundSource.Auto, 0));
        cells.Add(auto);

        foreach (var (argb, name) in LyricPosterSettings.BackgroundPalette)
        {
            var button = CreateSwatchButton(CreateDot(new SolidColorBrush(ToColor(argb))), name,
                () => ApplySettings(_settings with
                {
                    BackgroundSource = LyricPosterBackgroundSource.Preset,
                    BackgroundColorArgb = argb,
                }));
            _backgroundSwatches.Add((button, LyricPosterBackgroundSource.Preset, argb));
            cells.Add(button);
        }

        // 自定义色点：点圆点应用上次色轮颜色，点右下角铅笔打开色轮。
        _backgroundCustomDot = CreateDot(new SolidColorBrush(ToColor(_settings.BackgroundCustomColorArgb)));
        var (customCell, customApply, customPencil) = CreateCustomCell(
            _backgroundCustomDot,
            "应用上次自定义的背景色",
            "编辑自定义背景颜色",
            () => ApplySettings(_settings with
            {
                BackgroundSource = LyricPosterBackgroundSource.Custom,
                BackgroundColorArgb = _settings.BackgroundCustomColorArgb,
            }));
        customPencil.Click += (_, _) => _ = OpenBackgroundColorFlyoutAsync(customPencil);
        _backgroundSwatches.Add((customApply, LyricPosterBackgroundSource.Custom, 0));
        cells.Add(customCell);

        AddSwatchRows(BackgroundSwatchPanel, cells);
    }

    private void BuildTextSwatches()
    {
        var cells = new List<FrameworkElement>();

        _autoTextDot = CreateDot(new SolidColorBrush(
            ToColor(LyricPosterColorPolicy.AutoText(ToRgb(LyricPosterSettings.DefaultBackgroundColor)))));
        var auto = CreateSwatchButton(_autoTextDot, "按背景亮度自动选择文字颜色",
            () => ApplySettings(_settings with { TextColorMode = LyricPosterTextColorMode.Auto }));
        _textSwatches.Add((auto, LyricPosterTextColorMode.Auto, 0, false));
        cells.Add(auto);
        var white = CreateSwatchButton(CreateDot(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF))), "白色",
            () => ApplySettings(_settings with { TextColorMode = LyricPosterTextColorMode.White }));
        _textSwatches.Add((white, LyricPosterTextColorMode.White, 0xFFFFFFFF, false));
        cells.Add(white);

        var black = CreateSwatchButton(CreateDot(new SolidColorBrush(Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A))), "黑色",
            () => ApplySettings(_settings with { TextColorMode = LyricPosterTextColorMode.Black }));
        _textSwatches.Add((black, LyricPosterTextColorMode.Black, 0xFF1A1A1A, false));
        cells.Add(black);

        foreach (var (argb, name) in LyricPosterSettings.TextColorPalette)
        {
            var button = CreateSwatchButton(CreateDot(new SolidColorBrush(ToColor(argb))), name,
                () => ApplySettings(_settings with
                {
                    TextColorMode = LyricPosterTextColorMode.Custom,
                    TextColorArgb = argb,
                }));
            _textSwatches.Add((button, LyricPosterTextColorMode.Custom, argb, false));
            cells.Add(button);
        }

        _textCustomDot = CreateDot(new SolidColorBrush(ToColor(_settings.TextCustomColorArgb)));
        var (customCell, customApply, customPencil) = CreateCustomCell(
            _textCustomDot,
            "应用上次自定义的文字颜色",
            "编辑自定义文字颜色",
            () => ApplySettings(_settings with
            {
                TextColorMode = LyricPosterTextColorMode.Custom,
                TextColorArgb = _settings.TextCustomColorArgb,
            }));
        customPencil.Click += (_, _) => _ = OpenTextColorFlyoutAsync(customPencil);
        _textSwatches.Add((customApply, LyricPosterTextColorMode.Custom, 0, true));
        cells.Add(customCell);

        AddSwatchRows(TextColorSwatchPanel, cells);
    }

    private static Border CreateDot(Brush background) => new()
    {
        Width = 22,
        Height = 22,
        CornerRadius = new CornerRadius(11),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x80, 0x80, 0x80)),
        BorderThickness = new Thickness(1),
        Background = background,
    };

    private Button CreateSwatchButton(Border dot, string name, Action onClick)
    {
        var button = new Button
        {
            Width = 32,
            Height = 32,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(5),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(TransparentColor),
            BorderThickness = new Thickness(2),
            BorderBrush = _clearBrush,
            Content = dot,
        };

        ToolTipService.SetToolTip(button, name);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => onClick();
        return button;
    }

    private (Grid Cell, Button ApplyButton, Button PencilButton) CreateCustomCell(
        Border dot, string applyName, string pencilName, Action apply)
    {
        var applyButton = CreateSwatchButton(dot, applyName, apply);

        var pencil = new Button
        {
            Width = 14,
            Height = 14,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Content = new Icon
            {
                Data = IconPath("IconEdit"),
                Size = 7,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            },
        };
        ToolTipService.SetToolTip(pencil, pencilName);
        AutomationProperties.SetName(pencil, pencilName);

        var cell = new Grid { Width = 32, Height = 32 };
        cell.Children.Add(applyButton);
        cell.Children.Add(pencil);
        return (cell, applyButton, pencil);
    }

    private static string IconPath(string key)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is string path
            ? path
            : string.Empty;

    private static void AddSwatchRows(StackPanel host, IReadOnlyList<FrameworkElement> cells, int perRow = 6)
    {
        for (var start = 0; start < cells.Count; start += perRow)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
            };

            for (var index = start; index < Math.Min(start + perRow, cells.Count); index++)
            {
                row.Children.Add(cells[index]);
            }

            host.Children.Add(row);
        }
    }

    private void ApplySettings(LyricPosterSettings next)
    {
        if (_initializing)
        {
            return;
        }

        _settings = next.Normalized();
        Context.SettingsStore.Save(_settings);
        UpdateSwatchSelection();
        UpdateContrastHint();

        if (PreviewStage.Visibility == Visibility.Visible)
        {
            _ = RenderLatestAsync();
        }
    }

    private void UpdateSwatchSelection()
    {
        if (_backgroundCustomDot is not null)
        {
            _backgroundCustomDot.Background = new SolidColorBrush(ToColor(_settings.BackgroundCustomColorArgb));
        }

        if (_textCustomDot is not null)
        {
            _textCustomDot.Background = new SolidColorBrush(ToColor(_settings.TextCustomColorArgb));
        }

        foreach (var (button, source, argb) in _backgroundSwatches)
        {
            var selected = _settings.BackgroundSource switch
            {
                LyricPosterBackgroundSource.Auto => source == LyricPosterBackgroundSource.Auto,
                LyricPosterBackgroundSource.Preset => source == LyricPosterBackgroundSource.Preset
                    && _settings.BackgroundColorArgb == argb,
                LyricPosterBackgroundSource.Custom => source == LyricPosterBackgroundSource.Custom,
                _ => false,
            };

            button.BorderBrush = selected ? _accentBrush : _clearBrush;
        }

        foreach (var (button, mode, argb, isCustom) in _textSwatches)
        {
            var selected = _settings.TextColorMode switch
            {
                LyricPosterTextColorMode.Auto => mode == LyricPosterTextColorMode.Auto,
                LyricPosterTextColorMode.White => mode == LyricPosterTextColorMode.White,
                LyricPosterTextColorMode.Black => mode == LyricPosterTextColorMode.Black,
                LyricPosterTextColorMode.Custom => isCustom
                    ? _settings.TextColorArgb == _settings.TextCustomColorArgb
                    : _settings.TextColorArgb == argb,
                _ => false,
            };

            button.BorderBrush = selected ? _accentBrush : _clearBrush;
        }
    }

    private void UpdateContrastHint()
    {
        var background = _settings.BackgroundSource == LyricPosterBackgroundSource.Auto
            ? (_autoBackgroundTask?.IsCompletedSuccessfully == true
                ? _autoBackgroundTask.Result
                : ToRgb(LyricPosterSettings.DefaultBackgroundColor))
            : ToRgb(_settings.BackgroundColorArgb);

        var light = LyricPosterColorPolicy.IsLight(background);
        ContrastHint.Text = light
            ? "浅色背景：自动文字为深色，品牌图标使用深色版。"
            : "深色背景：自动文字为白色，品牌图标使用浅色版。";

        if (_settings.TextColorMode != LyricPosterTextColorMode.Auto)
        {
            ContrastHint.Text += " 当前为手动文字颜色。";
        }

        if (_autoTextDot is not null)
        {
            _autoTextDot.Background = new SolidColorBrush(
                ToColor(LyricPosterColorPolicy.AutoText(background)));
        }
    }

    private async Task InitializeAutoSwatchAsync()
    {
        var color = await EnsureAutoBackgroundAsync();

        if (_autoBackgroundDot is not null)
        {
            _autoBackgroundDot.Background = new SolidColorBrush(ToColor(color));
        }

        UpdateContrastHint();
    }

    private Task<RgbColor> EnsureAutoBackgroundAsync()
        => _autoBackgroundTask ??= LoadAutoBackgroundAsync();

    private async Task<RgbColor> LoadAutoBackgroundAsync()
    {
        var seeds = await Context.PaletteLoader.LoadAsync(Context.Track.Id, Context.Track.CoverImage);
        return ColorPalette.SolidBackground(seeds, AmbientTheme.Dark);
    }

    private async Task<LyricPosterStyle> ResolveStyleAsync()
    {
        var background = _settings.BackgroundSource == LyricPosterBackgroundSource.Auto
            ? await EnsureAutoBackgroundAsync()
            : ToRgb(_settings.BackgroundColorArgb);

        var text = _settings.TextColorMode switch
        {
            LyricPosterTextColorMode.White => LyricPosterColorPolicy.WhiteText,
            LyricPosterTextColorMode.Black => LyricPosterColorPolicy.BlackText,
            LyricPosterTextColorMode.Custom => ToRgb(_settings.TextColorArgb),
            _ => LyricPosterColorPolicy.AutoText(background),
        };

        return new LyricPosterStyle(
            _settings.FontFamily,
            ToColor(background),
            ToColor(text),
            LyricPosterColorPolicy.UseLightLogo(background));
    }

    private void OnFontPickerChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (_initializing)
        {
            return;
        }

        var family = PosterFontPicker.SelectedIndex >= 0
            && PosterFontPicker.SelectedIndex < Context.FontOptions.Count
            ? Context.FontOptions[PosterFontPicker.SelectedIndex].FamilyName
            : string.Empty;

        ApplySettings(_settings with { FontFamily = family });
    }

    private int FindFontIndex(string fontFamily)
    {
        for (var index = 0; index < Context.FontOptions.Count; index++)
        {
            if (string.Equals(Context.FontOptions[index].FamilyName, fontFamily, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private async Task OpenBackgroundColorFlyoutAsync(FrameworkElement anchor)
    {
        try
        {
            var background = _settings.BackgroundSource == LyricPosterBackgroundSource.Auto
                ? await EnsureAutoBackgroundAsync()
                : ToRgb(_settings.BackgroundColorArgb);
            var text = _settings.TextColorMode switch
            {
                LyricPosterTextColorMode.White => LyricPosterColorPolicy.WhiteText,
                LyricPosterTextColorMode.Black => LyricPosterColorPolicy.BlackText,
                LyricPosterTextColorMode.Custom => ToRgb(_settings.TextColorArgb),
                _ => LyricPosterColorPolicy.AutoText(background),
            };

            var (preview, apply) = CreateBackgroundPreview(ToColor(background), ToColor(text));

            var chosen = await ColorPickerFlyout.ShowAsync(
                anchor,
                ToColor(_settings.BackgroundCustomColorArgb),
                Context.Theme,
                preview,
                apply,
                ContrastHintForColor,
                "背景颜色",
                Context.Logger);

            if (chosen is { } argb)
            {
                ApplySettings(_settings with
                {
                    BackgroundSource = LyricPosterBackgroundSource.Custom,
                    BackgroundColorArgb = argb,
                    BackgroundCustomColorArgb = argb,
                });
            }
        }
        catch (Exception exception)
        {
            Context.Logger?.LogWarning(exception, "打开海报背景色轮失败");
        }
    }

    private async Task OpenTextColorFlyoutAsync(FrameworkElement anchor)
    {
        try
        {
            var background = _settings.BackgroundSource == LyricPosterBackgroundSource.Auto
                ? await EnsureAutoBackgroundAsync()
                : ToRgb(_settings.BackgroundColorArgb);

            var (preview, apply) = CreateTextPreview(ToColor(background), ToColor(_settings.TextCustomColorArgb));

            var chosen = await ColorPickerFlyout.ShowAsync(
                anchor,
                ToColor(_settings.TextCustomColorArgb),
                Context.Theme,
                preview,
                apply,
                TextContrastHintForColor,
                "文字颜色",
                Context.Logger);

            if (chosen is { } argb)
            {
                ApplySettings(_settings with
                {
                    TextColorMode = LyricPosterTextColorMode.Custom,
                    TextColorArgb = argb,
                    TextCustomColorArgb = argb,
                });
            }
        }
        catch (Exception exception)
        {
            Context.Logger?.LogWarning(exception, "打开海报文字色轮失败");
        }
    }

    private (FrameworkElement Preview, Action<Color> Apply) CreateBackgroundPreview(
        Color background, Color text)
    {
        var preview = new Border
        {
            Background = new SolidColorBrush(background),
            CornerRadius = new CornerRadius(8),
        };

        var title = new TextBlock
        {
            Text = Context.Track.Title,
            MaxWidth = 200,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(text),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var artist = new TextBlock
        {
            Text = Context.Track.ArtistText,
            MaxWidth = 200,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 14,
            Opacity = 0.65,
            Foreground = new SolidColorBrush(text),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var stack = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(title);
        stack.Children.Add(artist);
        preview.Child = stack;

        void Apply(Color color)
        {
            preview.Background = new SolidColorBrush(color);
            var foreground = new SolidColorBrush(
                ColorPickerDialog.RelativeLuminance(color) > 0.45
                    ? Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A)
                    : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
            title.Foreground = foreground;
            artist.Foreground = foreground;
        }

        return (preview, Apply);
    }

    private (FrameworkElement Preview, Action<Color> Apply) CreateTextPreview(Color background, Color text)
    {
        var preview = new Border
        {
            Background = new SolidColorBrush(background),
            CornerRadius = new CornerRadius(8),
        };

        var title = new TextBlock
        {
            Text = Context.Track.Title,
            MaxWidth = 200,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(text),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var artist = new TextBlock
        {
            Text = Context.Track.ArtistText,
            MaxWidth = 200,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 14,
            Opacity = 0.65,
            Foreground = new SolidColorBrush(text),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var stack = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(title);
        stack.Children.Add(artist);
        preview.Child = stack;

        void Apply(Color color)
        {
            title.Foreground = new SolidColorBrush(color);
            artist.Foreground = new SolidColorBrush(color);
        }

        return (preview, Apply);
    }

    private static string ContrastHintForColor(Color color)
        => ColorPickerDialog.RelativeLuminance(color) > 0.45
            ? "颜色偏亮，深色文字更清楚。"
            : "深色底，浅色文字更清楚。";

    private static string TextContrastHintForColor(Color color)
        => ColorPickerDialog.RelativeLuminance(color) > 0.45
            ? "浅色文字，适合深色背景。"
            : "深色文字，适合浅色背景。";

    private static RgbColor ToRgb(uint argb)
        => new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static Color ToColor(uint argb)
        => Color.FromArgb(0xFF, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static Color ToColor(RgbColor color)
        => Color.FromArgb(0xFF, color.R, color.G, color.B);

    // ── 选句 ───────────────────────────────────────────────────────────────

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
