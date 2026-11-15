namespace Bowen.Core.Models.AudioMemory;

/// <summary>
/// 个人页/月报 tab 的月报预览。
/// </summary>
/// <remarks>
/// <c>monthDesc</c> 只给月份、不给年份，定位年份要交给
/// <see cref="MonthPeriod.TryParseMonthDesc"/>。整个对象在账号没有任何月报时为 <c>null</c>。
/// </remarks>
public sealed record MonthReportSummary(
    string? MonthDesc,
    string? ListenTotalMinutes,
    string? ListenChain,
    Uri? Cover);
