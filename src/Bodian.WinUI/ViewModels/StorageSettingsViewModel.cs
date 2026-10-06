using System.Collections.ObjectModel;
using System.Diagnostics;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 设置页「存储」分区。
/// </summary>
/// <remarks>
/// <b>这一页只能清理可再生数据。</b> 凭据、窗口位置、各种偏好都不在里面 ——
/// 「清除缓存」不该把用户设过的东西一起清掉。
/// </remarks>
public sealed partial class StorageSettingsViewModel : ObservableObject
{
    private readonly IStorageMaintenanceService _storage;
    private readonly ILogger _logger;

    public StorageSettingsViewModel(
        IStorageMaintenanceService storage,
        ILogger<StorageSettingsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(storage);

        _storage = storage;
        _logger = logger ?? NullLogger<StorageSettingsViewModel>.Instance;

        Rows =
        [
            new StorageRowViewModel(
                StorageItemKind.CoverCache,
                "封面缓存",
                "看过的封面留在本地，断网也能显示；删掉不影响使用，只是下次要重新下"),
            new StorageRowViewModel(
                StorageItemKind.PlayHistory,
                "播放记录",
                "「最近播放」列表"),
            new StorageRowViewModel(
                StorageItemKind.SearchHistory,
                "搜索历史",
                "搜索框的建议列表"),
            new StorageRowViewModel(
                StorageItemKind.Logs,
                "日志",
                "排查问题用；当天那一份正在写入，不会被删",
                isLast: true),
        ];
    }

    public ObservableCollection<StorageRowViewModel> Rows { get; }

    /// <summary>数据目录，给「打开数据目录」用。</summary>
    public string RootDirectory => _storage.RootDirectory;

    /// <summary>重新统计每一项的占用。页面每次进入时调。</summary>
    public async Task MeasureAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StorageUsage> usages;

        try
        {
            usages = await _storage.MeasureAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            // 统计失败不该让整页空着：把四行留着，只是大小显示不出来。
            _logger.LogWarning(exception, "统计存储占用失败");
            return;
        }

        foreach (var row in Rows)
        {
            var usage = usages.FirstOrDefault(item => item.Item == row.Kind);
            row.SizeText = usage?.SizeText ?? "0 B";
        }
    }

    /// <summary>清掉一项。</summary>
    public async Task ClearAsync(StorageRowViewModel row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.IsBusy = true;
        row.ResultText = "";

        try
        {
            var outcome = await _storage.ClearAsync(row.Kind, cancellationToken).ConfigureAwait(true);
            row.ResultText = outcome.ResultText;

            // 封面缓存分两层：磁盘那份由服务清掉了，内存这份（已解码的位图）在界面层，
            // 得自己丢。丢掉不会让正在显示的封面消失 —— 位图已经挂在 Image.Source 上。
            if (row.Kind == StorageItemKind.CoverCache)
            {
                CoverImageCache.Clear();
            }

            await MeasureAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "清理 {Kind} 失败", row.Kind);
            row.ResultText = "清理失败";
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearAllAsync(CancellationToken cancellationToken)
    {
        foreach (var row in Rows)
        {
            await ClearAsync(row, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>在资源管理器里打开数据目录。</summary>
    /// <remarks>
    /// 用 <c>Process.Start</c> 而不是 <c>Launcher.LaunchFolderAsync</c>：
    /// 后者要一个 <c>StorageFolder</c>，对未打包应用拿目录的手续更绕。
    /// </remarks>
    [RelayCommand]
    private void OpenDataDirectory()
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
            Process.Start(new ProcessStartInfo(RootDirectory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "打开数据目录失败：{Path}", RootDirectory);
        }
    }
}
