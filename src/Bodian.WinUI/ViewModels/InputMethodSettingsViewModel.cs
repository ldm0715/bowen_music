using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bodian.WinUI.ViewModels;

public sealed partial class InputMethodSettingsViewModel : ObservableObject
{
    private readonly IInputMethodSettingsStore _store;
    private readonly NotificationViewModel _notifications;
    private bool _loading;

    public InputMethodSettingsViewModel(IInputMethodSettingsStore store, NotificationViewModel notifications)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(notifications);
        _store = store;
        _notifications = notifications;
        _loading = true;
        Enabled = store.Load().CompatibilityEnabled;
        _loading = false;
    }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    partial void OnEnabledChanged(bool oldValue, bool newValue)
    {
        if (_loading) return;
        if (_store.Save(new InputMethodSettings(newValue)))
        {
            _notifications.Show("输入法设置已保存，完全退出应用后重新打开生效");
            return;
        }
        _loading = true;
        try { Enabled = oldValue; }
        finally { _loading = false; }
        _notifications.Show("输入法设置保存失败", NoticeSeverity.Error);
    }
}
