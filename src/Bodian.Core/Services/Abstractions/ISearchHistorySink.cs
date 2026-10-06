namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 清空搜索历史时，把内存里那一份也清掉。
/// </summary>
/// <remarks>
/// <b>只调 store 落盘是不够的。</b> 搜索面板的 ViewModel 是单例，构造时把历史读进一个
/// 内存集合，之后只写不回读 —— 光落盘的话，当前会话里建议列表仍然是满的，要重启才消失。
/// 与 <c>INoticeSink</c> / <c>IQueueSink</c> 是同一套接线方式。
/// </remarks>
public interface ISearchHistorySink
{
    /// <summary>清空内存中的历史并落盘。与搜索面板那颗清空按钮走的是同一条路径。</summary>
    void ClearSearchHistory();
}
