namespace Bodian.WinUI.Services;

/// <summary>
/// 一行短提示的去处（目前是播放条上的那条）。
/// </summary>
/// <remarks>
/// 与 <see cref="ITrackNavigator"/> 同一个理由：把 WinUI 类型挡在 <c>TrackActionsViewModel</c> 之外。
/// </remarks>
public interface INoticeSink
{
    /// <summary>显示一条提示。<b>传空串表示不提示</b>（调用方已经滤过，这里不必再判）。</summary>
    void Show(string message);
}
