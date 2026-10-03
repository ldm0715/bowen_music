namespace Bodian.WinUI.Controls;

/// <summary>
/// 发现页横向卡片区的分页算式：把「可用宽度 / 卡片步长 / 卡片总数」换算成
/// 「一页放几张、当前该显示哪一段」。
/// </summary>
/// <remarks>
/// <b>刻意不引用任何 WinUI 类型</b>，所以能链进测试项目离屏验证。这里的边界 ——
/// 宽度为 0、宽度不足一步、换宽度时钉住锚点、末页夹取 —— 全是 off-by-one 高发区，
/// 放在 <c>HomeSectionView</c> 里就没法单独测了。
/// </remarks>
internal static class CardStripPaging
{
    /// <summary>
    /// 一页放得下几张。
    /// </summary>
    /// <remarks>
    /// 宽度不足一步、为 0 或还没量到时至少给 1 —— 那一张会溢出并被裁掉，
    /// 是允许的退化（`ScrollViewer` 已禁用，不会溢出成滚动）。
    /// </remarks>
    public static int PageSize(double availableWidth, double step)
    {
        if (step <= 0 || double.IsNaN(availableWidth) || availableWidth <= 0) return 1;
        return Math.Max(1, (int)Math.Floor(availableWidth / step));
    }

    /// <summary>按可用宽度与锚点解析出当前页。</summary>
    /// <param name="availableWidth">卡片区可用宽度。</param>
    /// <param name="step">一张卡片占的横向宽度（封面边长 + 间隔）。</param>
    /// <param name="cardCount">卡片总数。</param>
    /// <param name="anchorIndex">
    /// 希望换宽度后仍然出现在眼前的卡片下标，通常是「换宽度前当前页的第一张」。
    /// 传负数表示不锚定，回到第 0 页。
    /// </param>
    public static CardStripPage Resolve(double availableWidth, double step, int cardCount, int anchorIndex)
    {
        var size = PageSize(availableWidth, step);

        // 空列表也算一页：PageCount 为 0 会让下面的 Clamp 拿到 min > max，
        // 而空列表在 UI 上就是「一页，但没有东西」。
        var pageCount = cardCount <= 0 ? 1 : (int)Math.Ceiling(cardCount / (double)size);

        var anchor = anchorIndex < 0 ? 0 : Math.Min(anchorIndex, Math.Max(0, cardCount - 1));
        var pageIndex = Math.Clamp(anchor / size, 0, pageCount - 1);
        var firstIndex = pageIndex * size;
        var count = Math.Max(0, Math.Min(size, cardCount - firstIndex));
        return new CardStripPage(firstIndex, count, pageIndex, pageCount);
    }
}

/// <summary>一页的解析结果：从第几张开始、放几张、第几页、共几页。</summary>
internal readonly record struct CardStripPage(int FirstIndex, int Count, int PageIndex, int PageCount)
{
    /// <summary>还有上一页 —— 决定左箭头显不显示。</summary>
    public bool HasPrevious => PageIndex > 0;

    /// <summary>还有下一页 —— 决定右箭头显不显示。</summary>
    public bool HasNext => PageIndex < PageCount - 1;
}
