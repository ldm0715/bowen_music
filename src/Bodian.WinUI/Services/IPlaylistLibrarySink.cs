namespace Bodian.WinUI.Services;

/// <summary>
/// 页面告诉外壳「自建歌单的集合变了」。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="INoticeSink"/> 同一种做法：动作发生在页面里，但「侧栏那一段要跟着变」
/// 只有外壳知道 —— 侧栏列表归它管，删完还该决定当前这一页去哪。
/// </para>
/// <para>
/// 由 <c>MainWindow</c> 实现并注册成单例，注入给需要的 ViewModel。
/// </para>
/// </remarks>
public interface IPlaylistLibrarySink
{
    /// <summary>
    /// 某个自建歌单被删掉了。
    /// </summary>
    /// <remarks>
    /// 外壳负责两件事：把这一行从侧栏摘掉；如果当前正停在这个歌单的详情页上，
    /// 换根离开（那个页面是根页，<c>GoBack</c> 无处可去）。
    /// </remarks>
    void OnPlaylistRemoved(long playlistId);

    /// <summary>
    /// 某个自建歌单被改名或换了封面。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与删除同一动机：动作发生在详情页，但侧栏那一行归外壳管。
    /// <b>只改那一行的名字与封面，不重拉列表</b> —— 写请求已经成功，再拉一次是白跑。
    /// </para>
    /// <para>
    /// 歌单本身还在，所以**不导航**：用户还停在详情页上，让他继续待着。
    /// </para>
    /// </remarks>
    /// <param name="playlistId">被改的那个歌单。</param>
    /// <param name="name">新名字。</param>
    /// <param name="cover">新封面；<c>null</c> 表示没换。</param>
    void OnPlaylistUpdated(long playlistId, string name, Uri? cover);
}
