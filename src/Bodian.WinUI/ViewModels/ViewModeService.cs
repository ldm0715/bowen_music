using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 「用封面卡片还是行列表」这一个开关的**唯一持有者**。
/// </summary>
/// <remarks>
/// <para>
/// 单例。搜索结果的歌单 / 专辑 / 歌手三个页签与「收藏的专辑」「收藏的歌单」两页都直接绑它的
/// <see cref="UseGrid"/>，而不是各自在 ViewModel 里存一份 —— 存多份就要同步，漏一处就是
/// 「在收藏页切了卡片、回搜索页还是列表」，而搜索页的 ViewModel 恰恰是单例、活得更久。
/// </para>
/// <para>
/// 页面 ViewModel 只把它当属性透出去（XAML 里写 <c>ViewModel.ViewMode.UseGrid</c>）：
/// <c>x:Bind</c> 只要求最后一步可通知，而这一层是单例、实例不换，所以页面侧不需要任何订阅，
/// 也就没有「临时 ViewModel 订阅单例服务」那种泄漏。
/// </para>
/// </remarks>
public sealed partial class ViewModeService : ObservableObject
{
    private readonly IViewModeSettingsStore _store;
    private bool _useGrid;

    public ViewModeService(IViewModeSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;

        // 读一次就够：构造之后不再从文件读回来，改的都只是这一个属性。
        _useGrid = store.Load().UseGrid;
    }

    /// <summary>用封面卡片而不是行列表。改了立刻落盘。</summary>
    public bool UseGrid
    {
        get => _useGrid;
        set
        {
            if (!SetProperty(ref _useGrid, value))
            {
                return;
            }

            // 落盘在 setter 里，不在命令里：将来若有别的入口改这个值，也一样会记住。
            OnPropertyChanged(nameof(IsList));
            _store.Save(new ViewModeSettings { UseGrid = value });
        }
    }

    /// <summary>列表态。<see cref="UseGrid"/> 的反面，给 XAML 里叠加「当前是哪一页」用。</summary>
    /// <remarks>
    /// 单独给一个属性，而不是让 XAML 自己写 <c>!UseGrid</c>：<c>x:Bind</c> 的函数绑定不接受取反，
    /// 而 <c>Formats.VisibleBoth</c> 的两个参数都要求是现成的布尔。
    /// </remarks>
    public bool IsList => !_useGrid;

    [RelayCommand]
    private void Toggle() => UseGrid = !UseGrid;
}
