namespace Bodian.WinUI.Services;

/// <summary>
/// 页面上的数据<b>跟账号绑定</b>：换了账号必须重新拉一次。
/// </summary>
/// <remarks>
/// <para>
/// <b>这管的是「正在屏幕上的那一页」。</b> 那条路是「首次进入才请求」，而换账号时页面
/// <b>还在屏幕上、不会再走一次进场</b> —— 于是「我喜欢的」停在原账号那批歌上，
/// 用户只能自己点刷新。所以要由外壳主动叫一次。
/// </para>
/// <para>
/// <b>另半截在基类的「已加载」守卫里。</b> <c>PagedList&lt;T&gt;</c> 与
/// <c>PlaylistTracksViewModel</c> 的守卫判的是「<b>为哪个账号</b>加载过」，
/// 所以离开再回来（导航栈与闲置页面缓存会复用页面实例）会自己重拉，不需要这个接口。
/// 两者缺一不可：这里管当前页，那里管重进。
/// </para>
/// <para>
/// <b>只声明账号级页面。</b> 实体页（某个歌单 / 专辑 / 歌手）与本地页（设置、发现）不实现它，
/// 免得换号时把它们正在看的东西换掉。
/// </para>
/// </remarks>
public interface IAccountScopedView
{
    /// <summary>
    /// 账号变了：丢弃上一个账号的数据，按当前账号重拉。
    /// </summary>
    /// <returns>完成即重拉结束。外壳不 await（换号的界面动作不该等网络）。</returns>
    Task OnAccountSwitchedAsync();
}
