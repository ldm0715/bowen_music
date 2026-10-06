using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 「存储」那几项的占用统计与清理。
/// </summary>
/// <remarks>
/// 统计与清理放在一起，是因为设置页上每一行都是「显示多大 + 一个清空按钮」，
/// 拆成两个服务只会让页面同时注入两个。
/// </remarks>
public interface IStorageMaintenanceService
{
    /// <summary>数据根目录（<c>%LOCALAPPDATA%\Bodian</c>）。给「打开数据目录」用。</summary>
    string RootDirectory { get; }

    /// <summary>逐项统计占用。不存在的目录按 0 计，不抛。</summary>
    Task<IReadOnlyList<StorageUsage>> MeasureAsync(CancellationToken cancellationToken = default);

    /// <summary>清掉一项。</summary>
    Task<StorageClearOutcome> ClearAsync(StorageItemKind item, CancellationToken cancellationToken = default);

    /// <summary>逐项清掉。</summary>
    Task<IReadOnlyList<StorageClearOutcome>> ClearAllAsync(CancellationToken cancellationToken = default);
}
