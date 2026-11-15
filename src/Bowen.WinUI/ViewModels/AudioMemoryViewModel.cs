using Bowen.Core.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bowen.WinUI.ViewModels;

/// <summary>
/// 「音波回忆」页的页签宿主。
/// </summary>
/// <remarks>
/// 两个页签各自管数据，这里只负责切换与账号变更时的整体重置。
/// 页签内容都留在可视树上、只切 <c>Visibility</c>，切回来不会丢滚动位置。
/// </remarks>
public sealed partial class AudioMemoryViewModel : ObservableObject
{
    private readonly ICurrentAccount _account;
    private string? _loadedScope;

    public AudioMemoryViewModel(
        WeeklyFavoritesViewModel weekly,
        MonthlyReportViewModel report,
        ICurrentAccount account)
    {
        ArgumentNullException.ThrowIfNull(weekly);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(account);

        Weekly = weekly;
        Report = report;
        _account = account;
    }

    public IReadOnlyList<string> Tabs { get; } = ["本周最爱", "听歌月报"];

    public WeeklyFavoritesViewModel Weekly { get; }

    public MonthlyReportViewModel Report { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWeeklyTab))]
    [NotifyPropertyChangedFor(nameof(IsReportTab))]
    public partial int SelectedTab { get; set; }

    public bool IsWeeklyTab => SelectedTab == 0;

    public bool IsReportTab => SelectedTab == 1;

    /// <summary>
    /// 页面进入时只加载当前页签；另一个等切过去再加载。
    /// </summary>
    /// <remarks>
    /// <b>这里同时负责账号守卫。</b> 页面实例会被导航服务缓存，换号时它可能不在屏幕上，
    /// 外壳不会通知它；等用户再进来时，必须按当前账号的 scope 判断旧数据还能不能用。
    /// </remarks>
    public Task EnsureLoadedAsync()
    {
        var scope = _account.Scope;

        if (_loadedScope != scope)
        {
            Weekly.Reset();
            Report.Reset();
            _loadedScope = scope;
        }

        return SelectedTab == 0
            ? Weekly.EnsureLoadedAsync()
            : Report.EnsureLoadedAsync();
    }

    partial void OnSelectedTabChanged(int value) => _ = EnsureLoadedAsync();

    /// <summary>换账号：两个页签一起丢数据。页面仍在屏幕上时由外壳调用。</summary>
    public void ResetForAccountChange()
    {
        Weekly.Reset();
        Report.Reset();
        _loadedScope = _account.Scope;
    }
}
