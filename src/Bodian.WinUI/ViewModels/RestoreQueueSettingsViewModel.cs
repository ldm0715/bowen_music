using Bodian.WinUI.Playback;
using Bodian.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 「记住播放列表」开关。
/// </summary>
/// <remarks>
/// <b>薄壳</b>：开关的真值在 <see cref="PlaybackCoordinator"/> 里 —— 它才是读盘、落盘、决定
/// 内存里那条队列算不算数的那一方。这里只接住界面上的拨动，并把结果如实告诉用户。
/// 写法与输入法兼容开关同款：装载中守卫 + 保存失败回滚。
/// </remarks>
public sealed partial class RestoreQueueSettingsViewModel : ObservableObject
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly NotificationViewModel _notifications;
    private bool _loading;

    public RestoreQueueSettingsViewModel(PlaybackCoordinator coordinator, NotificationViewModel notifications)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(notifications);
        _coordinator = coordinator;
        _notifications = notifications;

        _loading = true;
        Enabled = coordinator.RestoreQueueEnabled;
        _loading = false;
    }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    partial void OnEnabledChanged(bool oldValue, bool newValue)
    {
        if (_loading) return;

        if (_coordinator.SetRestoreQueueEnabled(newValue))
        {
            return;
        }

        // 保存失败：开关拨回去，并如实告知。
        _loading = true;
        try { Enabled = oldValue; }
        finally { _loading = false; }
        _notifications.Show("设置保存失败", NoticeSeverity.Error);
    }
}
