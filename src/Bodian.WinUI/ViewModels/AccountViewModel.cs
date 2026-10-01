using Bodian.Core.Api;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 侧栏底部的账号卡片。
/// </summary>
/// <remarks>
/// <b>属性必须在账号变化时自己发通知。</b> 侧栏是主窗口构造时就建好的，那一刻还没有会话
/// （登录页还没走完），而 <c>x:Bind</c> 默认是 OneTime —— 不发通知的话，
/// 登录完成后头像与昵称会永远停在空白处。原来这几个属性挂在搜索页上没暴露这个问题，
/// 因为搜索页是在登录之后才构造的。
/// </remarks>
public sealed partial class AccountViewModel : ObservableObject
{
    private readonly IBodianLogin _login;

    public AccountViewModel(IBodianLogin login)
    {
        ArgumentNullException.ThrowIfNull(login);

        _login = login;
        _login.AccountChanged += OnAccountChanged;
    }

    /// <summary>账号显示名。没有昵称就退回 uid。</summary>
    public string AccountText => _login.Nickname ?? _login.Account?.Uid ?? "已登录";

    /// <summary>账号头像。没有（或老凭据文件里没存）时为 <c>null</c>，界面显示占位。</summary>
    public ImageSource? Avatar
    {
        get
        {
            var uri = _login.Account?.Avatar;

            // BitmapImage 会自己异步加载；地址失效时图是空的，不影响布局。
            return uri is null ? null : new BitmapImage(uri);
        }
    }

    /// <summary>是否会员。**只用于展示**，播放权限一律以服务端 checkRight 为准。</summary>
    public bool IsVip => _login.Account?.IsVip == true;

    /// <summary>
    /// 退出登录。
    /// </summary>
    /// <remarks>
    /// 这里只管清会话，跳转由宿主负责 —— 登出会让 <c>AccountChanged</c> 触发，
    /// 主窗口收到后把页面切回登录页。不在这里重复导航，避免两处同时切页。
    /// </remarks>
    [RelayCommand]
    private void SignOut() => _login.SignOut();

    private void OnAccountChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AccountText));
        OnPropertyChanged(nameof(Avatar));
        OnPropertyChanged(nameof(IsVip));
    }
}
