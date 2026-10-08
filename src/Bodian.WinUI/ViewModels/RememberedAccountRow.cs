using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 「切换账号」列表里的一行。
/// </summary>
/// <remarks>
/// <para>
/// <b>纯数据行，不带命令。</b> 切换与移除都由窗口的 code-behind 发起 —— 切换成功后要把弹层收起来
/// （<c>XamlRoot</c> 与 <c>Flyout</c> 都拿不到视图模型里），移除要弹确认框。两个动作都要视图参与，
/// 那就不必再绕一层命令。
/// </para>
/// <para>
/// <b>这里不含 token。</b> 行只带界面要用的那几样（昵称、头像、uid），凭据留在 Core 里。
/// 带进来会让「界面对象持有凭据」变成一件很难回头的事。
/// </para>
/// </remarks>
public sealed class RememberedAccountRow
{
    public RememberedAccountRow(RememberedAccount entry, bool isCurrent, bool isStale)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Uid = entry.Credential.Uid;
        DisplayName = entry.Credential.Nickname is { Length: > 0 } nickname ? nickname : Uid;
        IsCurrent = isCurrent;
        IsStale = isStale;

        // 与标题栏账号头像同一张缩略图（同一个大小），避免每行再解码一张原尺寸。
        Avatar = CoverImageCache.Get(ParseAvatar(entry.Credential.AvatarUrl), 128);
    }

    /// <summary>
    /// 头像地址转 <see cref="Uri"/>。<b>只认 http/https</b>。
    /// </summary>
    /// <remarks>
    /// Core 里那个 <c>HttpUrl</c> 是 internal，这里够不着；而放开了随便什么 scheme 进来，
    /// 一条 <c>file://</c> 就会被本地缓存层当成图片源去取。地址来自服务端，仍然按外部输入对待。
    /// </remarks>
    private static Uri? ParseAvatar(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;

    public string Uid { get; }

    /// <summary>显示名。没有昵称就退回 uid —— 老凭据可能没存昵称。</summary>
    public string DisplayName { get; }

    /// <summary>头像。没有（或老凭据里没存）时为 <c>null</c>，界面显示占位。</summary>
    public ImageSource? Avatar { get; }

    /// <summary>是不是当前正在用的那个。当前那个不显示勾选以外的动作。</summary>
    public bool IsCurrent { get; }

    /// <summary>本次运行期间被服务端判死过。见 <c>IBodianLogin.IsStale</c>。</summary>
    public bool IsStale { get; }

    /// <summary>当前这行不再提供切换（切到自己没意义）。界面据此禁掉整行。</summary>
    public bool CanSwitch => !IsCurrent;

    /// <summary>当前账号那一行的底色。<b>用底色而不是勾</b>，与托盘播放模式菜单同一套做法。</summary>
    public Visibility CurrentHighlightVisibility => Visible(IsCurrent);

    public Visibility StaleLabelVisibility => Visible(IsStale);

    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
}
