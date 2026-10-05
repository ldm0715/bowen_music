namespace Bodian.WinUI.Services;

/// <summary>
/// 一条短提示的语气。决定通知条上的图标配色，以及它该挂多久。
/// </summary>
/// <remarks>
/// 定义在本文件里而不是单开一个文件：本文件被 link 进离线测试工程
/// （<c>tests/Bodian.Core.Tests.csproj</c>），新开文件要同步改那边。
/// </remarks>
public enum NoticeSeverity
{
    /// <summary>中性消息：「已在播放队列」「这批歌正在处理中」这类，没有成功失败之分。</summary>
    Informational,

    /// <summary>操作成功。</summary>
    Success,

    /// <summary>操作失败，或用户当前做不了（未登录、无权限）。</summary>
    Error,
}

/// <summary>
/// 一行短提示的去处（目前是播放条上方那条浮层通知）。
/// </summary>
/// <remarks>
/// 与 <see cref="ITrackNavigator"/> 同一个理由：把 WinUI 类型挡在 <c>TrackActionsViewModel</c> 之外。
/// </remarks>
public interface INoticeSink
{
    /// <summary>显示一条提示。<b>传空串表示不提示</b>（调用方已经滤过，这里不必再判）。</summary>
    void Show(string message, NoticeSeverity severity = NoticeSeverity.Informational);
}
