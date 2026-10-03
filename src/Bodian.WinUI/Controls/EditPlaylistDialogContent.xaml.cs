using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 「编辑歌单」对话框的内容体：封面、标题、介绍、标签。
/// </summary>
/// <remarks>
/// <para>
/// <b>只做界面与本地草稿，不碰网络。</b> 保存是 <c>PlaylistDetailViewModel.SaveEditAsync</c> 的事 ——
/// 与取消收藏、删除同一条规矩：弹窗是界面决策，动作归 ViewModel。
/// </para>
/// <para>
/// <b>裁剪器内嵌，不另开对话框。</b> 同一个 <c>XamlRoot</c> 上同时只允许一个
/// <see cref="ContentDialog"/>，再嵌一个会直接抛；所以选完图是把这个对话框的内容
/// 切成裁剪器，而不是叠一层。
/// </para>
/// <para>
/// <b>字段与官方编辑页一一对应</b>：标题、介绍、封面、标签。
/// <b>没有隐私开关</b> —— 官方客户端创建之后就改不了隐私，我们也不假装能改。
/// </para>
/// </remarks>
public sealed partial class EditPlaylistDialogContent : UserControl
{
    /// <summary>标签上限。官方文案是「最多选择三个标签」，服务端也按这个收。</summary>
    private const int MaxTags = 3;

    private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".bmp"];

    private readonly IWindowHandleProvider _handles;
    private readonly List<TagChip> _chips = [];

    private byte[]? _coverBytes;

    public EditPlaylistDialogContent(IWindowHandleProvider handles)
    {
        ArgumentNullException.ThrowIfNull(handles);

        _handles = handles;

        InitializeComponent();

        // 预览显示失败不是致命的 —— 裁剪本身照样能出图（它用的是解码后的位图，
        // 与画面上那张预览无关）。所以这里只提示，不把用户踢回表单。
        Cropper.PreviewFailed += (_, _) => ShowError("预览显示不出来，但可以直接点「完成裁剪」取图。");
    }

    /// <summary>内容或裁剪态变了，宿主据此重算主按钮的可用性。</summary>
    public event EventHandler? StateChanged;

    /// <summary>新封面的 JPEG 字节。<c>null</c> 表示这次没换封面。</summary>
    public byte[]? CoverBytes => _coverBytes;

    /// <summary>标题。空白由服务端那层挡（<c>BodianApi</c> 会抛），这里只管置灰按钮。</summary>
    public string PlaylistName => NameBox.Text.Trim();

    public string PlaylistDescription => DescriptionBox.Text;

    /// <summary>选中的标签，最多 3 个。</summary>
    public IReadOnlyList<MusicCategory> SelectedCategories =>
        [.. _chips.Where(c => c.IsSelected).Select(c => c.ToCategory())];

    /// <summary>现在是不是在裁剪。裁剪期间主按钮要置灰 —— 那会儿「保存」没意义。</summary>
    public bool IsCropping => CropPanel.Visibility == Visibility.Visible;

    /// <summary>「保存」能不能点。</summary>
    public bool CanSave => !IsCropping && !string.IsNullOrWhiteSpace(NameBox.Text);

    /// <summary>
    /// 填初值。**编辑界面必须显示歌单当前的样子**，否则用户一进去看到的是空白，
    /// 会以为原来的内容没了。
    /// </summary>
    /// <param name="groups">标签候选，按组给我。</param>
    /// <param name="selected">歌单当前的标签。</param>
    /// <param name="currentCover">当前封面，可能没有。</param>
    /// <param name="name">歌单当前的名字。</param>
    /// <param name="description">歌单当前的简介。</param>
    public void Initialize(
        IReadOnlyList<CategoryGroup> groups,
        IReadOnlyList<MusicCategory> selected,
        Uri? currentCover,
        string name,
        string description)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(selected);

        NameBox.Text = name;
        DescriptionBox.Text = description;

        if (currentCover is not null)
        {
            CoverPreview.Source = new BitmapImage(currentCover);
        }

        BuildTagGroups(groups, selected);
        RaiseStateChanged();
    }

    private void BuildTagGroups(IReadOnlyList<CategoryGroup> groups, IReadOnlyList<MusicCategory> selected)
    {
        var selectedIds = selected.Select(c => c.Id).ToHashSet();

        TagGroupsPanel.Children.Clear();
        _chips.Clear();

        foreach (var group in groups)
        {
            if (group.SubCategories.Count == 0)
            {
                continue;
            }

            TagGroupsPanel.Children.Add(new TextBlock
            {
                Text = group.Name,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 4),
                Opacity = 0.75,
            });

            var chips = new List<TagChip>();

            foreach (var category in group.SubCategories)
            {
                var chip = new TagChip(category) { IsSelected = selectedIds.Contains(category.Id) };
                chips.Add(chip);
                _chips.Add(chip);
            }

            TagGroupsPanel.Children.Add(BuildTagRow(chips));
        }
    }

    /// <summary>
    /// 一组的 chip。<b>用 <see cref="ItemsControl"/> + <see cref="WrapPanel"/></b>，
    /// 不用 <c>GridView</c> —— 后者给一屏里所有条目用同一个格子尺寸，长标签会被裁掉。
    /// </summary>
    private ItemsControl BuildTagRow(IReadOnlyList<TagChip> chips) => new()
    {
        ItemsSource = chips,
        ItemTemplate = (DataTemplate)Resources["TagChipTemplate"],
        ItemsPanel = (ItemsPanelTemplate)Resources["TagChipPanelTemplate"],
    };

    // ── 标签选中 ────────────────────────────────────────────────────────────

    private void OnChipChecked(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleButton button || button.Tag is not TagChip chip || chip.IsSelected)
        {
            return;
        }

        if (_chips.Count(c => c.IsSelected) >= MaxTags)
        {
            // 选满了：把这一下弹回去，并就地说明原因 —— 比静默忽略要好。
            //
            // ★ 必须直接改 ToggleButton，不能只把 chip.IsSelected 置回 false：
            //   它本来就是 false，赋同样的值不会发通知，OneWay 绑定也就不会把
            //   ToggleButton 的选中态收回去 —— 那个 chip 会一直显示为选中，
            //   但按选中集合算它根本没被选上，界面与状态就对不上了。
            button.IsChecked = false;
            ShowError($"最多选择 {MaxTags} 个标签。");
            return;
        }

        chip.IsSelected = true;
        ClearError();
    }

    private void OnChipUnchecked(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleButton { Tag: TagChip chip })
        {
            return;
        }

        chip.IsSelected = false;

        // 取消一个之后就没到上限了，「最多 3 个」那句话不再成立，别留着误导。
        // 注意顺序：兜底那条路是「先 IsChecked=false 再 ShowError」，所以这里清掉的
        // 不会把刚弹出的提示一起抹掉。
        ClearError();
    }

    // ── 封面 ────────────────────────────────────────────────────────────────

    private async void OnPickCoverClick(object sender, RoutedEventArgs args)
    {
        var bytes = await PickImageAsync();

        if (bytes is null)
        {
            return;
        }

        await StartCroppingAsync(bytes);
    }

    private async Task<byte[]?> PickImageAsync()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            };

            foreach (var extension in SupportedExtensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            // 桌面端必须先把选择器绑到宿主窗口，否则一打开就抛。
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _handles.WindowHandle);

            if (await picker.PickSingleFileAsync() is not { } file)
            {
                return null;
            }

            using var stream = await file.OpenStreamForReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);

            return memory.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowError("打不开系统选择器，稍后再试。");
            return null;
        }
    }

    private async Task StartCroppingAsync(byte[] bytes)
    {
        ClearError();

        // ★ 先切到裁剪视图再解码：ImageCropView 得先量到视口尺寸，解码完的图才有地方摆。
        //   反过来做的话视口一直是 0×0，图片宽度被算成 0，看着就是「图没出来」。
        FormPanel.Visibility = Visibility.Collapsed;
        CropPanel.Visibility = Visibility.Visible;
        RaiseStateChanged();

        if (await Cropper.LoadAsync(bytes))
        {
            return;
        }

        // 解不开就退回表单，别把用户留在块空白上。
        FormPanel.Visibility = Visibility.Visible;
        CropPanel.Visibility = Visibility.Collapsed;
        RaiseStateChanged();
        ShowError("这张图片读不出来，换一张试试（支持 JPG / PNG / BMP）。");
    }

    private async void OnCropDoneClick(object sender, RoutedEventArgs args)
    {
        byte[]? jpeg;

        try
        {
            jpeg = await Cropper.RenderAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 出图的异常必须在这里接住：这是个 async void 的点击处理器，
            // 让它跑出去就是整个应用闪退（Win2D 的 COMException 就这么干过一次）。
            Debug.WriteLine($"裁剪出图失败：{ex}");
            ShowError("裁剪出图失败，换一张图试试。");
            return;
        }

        if (jpeg is null)
        {
            ShowError("裁剪结果为空，换一张试试。");
            return;
        }

        _coverBytes = jpeg;

        CoverPreview.Source = await CreatePreviewAsync(jpeg);

        FormPanel.Visibility = Visibility.Visible;
        CropPanel.Visibility = Visibility.Collapsed;
        RaiseStateChanged();
    }

    private async void OnRepickClick(object sender, RoutedEventArgs args)
    {
        var bytes = await PickImageAsync();

        if (bytes is null)
        {
            return;
        }

        await StartCroppingAsync(bytes);
    }

    /// <summary>把裁剪结果当预览显示。字节已经在手上，不必先传上去再拉回来。</summary>
    private static async Task<BitmapImage> CreatePreviewAsync(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());

        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);

        return bitmap;
    }

    // ── 状态 ────────────────────────────────────────────────────────────────

    private void OnNameTextChanged(object sender, TextChangedEventArgs args) => RaiseStateChanged();

    private void ShowError(string message)
    {
        CoverErrorText.Text = message;
        CoverErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        CoverErrorText.Visibility = Visibility.Collapsed;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
