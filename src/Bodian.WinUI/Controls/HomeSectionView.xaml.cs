using Bodian.Core.Models.Home;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 发现页里的一组卡片。
/// </summary>
/// <remarks>
/// <b>它只是个视图，不认识门面。</b> 点卡片只把 <see cref="HomeCard"/> 抛给页面，
/// 由页面决定「曲目就播、歌单就进详情」—— 控件不该知道导航与播放的存在。
/// </remarks>
public sealed partial class HomeSectionView : UserControl
{
    /// <summary>要显示的那一组。绑到 XAML 的 <c>Section</c>。</summary>
    public static readonly DependencyProperty SectionProperty =
        DependencyProperty.Register(
            nameof(Section),
            typeof(HomeSection),
            typeof(HomeSectionView),
            new PropertyMetadata(null, OnSectionChanged));

    public HomeSectionView()
    {
        InitializeComponent();
    }

    /// <summary>用户点了某张卡片。</summary>
    public event EventHandler<HomeCard>? CardInvoked;

    /// <summary>
    /// 用户点了这一组的标题。参数是**整组** —— 调用方要用它的 <c>AiIndex</c> 与标题。
    /// </summary>
    /// <remarks>只有能打开成完整歌单的组（<c>Ai</c> 有值）才是可点的，见 <see cref="IsOpenable"/>。</remarks>
    public event EventHandler<HomeSection>? SectionInvoked;

    public HomeSection? Section
    {
        get => (HomeSection?)GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    /// <summary>
    /// 有没有小标题。
    /// </summary>
    /// <remarks>
    /// 给 <c>x:Bind</c> 用。<b>直接返回 <see cref="Visibility"/></b> ——
    /// 函数绑定不接受 <c>Converter</c>（会报 WMC1121），返回 bool 绑不到 <c>Visibility</c> 上。
    /// </remarks>
    private bool HasTitle => !string.IsNullOrWhiteSpace(Section?.Title);

    /// <summary>
    /// 这一组的标题可不可点。
    /// </summary>
    /// <remarks>
    /// <b>「个性化歌单」与「你的主题歌单」的分组都会带 <c>Ai</c></b>：每组其实是一个完整歌单，
    /// 模块里给的 3 首只是预览，点标题能取到完整的（实测都是 30 首）。
    /// 其余分组没有可进去的地方，标题就不该看起来可点。
    /// </remarks>
    private bool IsOpenable => Section?.Ai is not null;

    /// <summary>有标题、但这一组不可点 —— 那种情况用纯文本画标题。</summary>
    private bool HasPlainTitle => HasTitle && !IsOpenable;

    private static void OnSectionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        // Section 是整体换的（不是内部可变），所以换值时手动补一次通知，
        // 让依赖 Section.Title 的 HasTitle 跟着刷新。
        if (sender is HomeSectionView view)
        {
            view.Bindings.Update();
        }
    }

    private void OnTitleClick(object sender, RoutedEventArgs e)
    {
        if (Section is { Ai: not null } section)
        {
            SectionInvoked?.Invoke(this, section);
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeCard card)
        {
            CardInvoked?.Invoke(this, card);
        }
    }
}
