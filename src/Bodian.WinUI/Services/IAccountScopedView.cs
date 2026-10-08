namespace Bodian.WinUI.Services;

/// <summary>
/// 页面上的数据<b>跟账号绑定</b>：换了账号必须重新拉一次。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不复用 <see cref="INavigationAware.OnNavigatedTo"/>。</b> 那条路是「首次进入才请求」
/// （各页面的守卫记着「我加载过了」），而换账号时页面<b>还在屏幕上、不会再走一次进场</b> ——
/// 于是「我喜欢的」停在原账号那批歌上，用户只能自己点刷新。所以要由外壳主动叫一次。
/// </para>
/// <para>
/// <b>只声明账号级页面。</b> 实体页（某个歌单 / 专辑 / 歌手）与本地页（设置、发现）不实现它：
/// 重新拉一次要么白费一个请求，要么把用户正在看的东西换掉。
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
