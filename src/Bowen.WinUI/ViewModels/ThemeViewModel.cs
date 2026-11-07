using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Bowen.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace Bowen.WinUI.ViewModels;

/// <summary>
/// 顶栏的外观切换。
/// </summary>
/// <remarks>
/// <para>
/// <b>主题在构造函数里同步读出来</b>，不等任何异步初始化。主窗口是在启动路径上构造的，
/// 异步读会让第一帧先按系统主题渲染、再跳到用户选的那套 —— 浅色启动闪深色的观感很差。
/// </para>
/// <para>
/// <b>为什么用 <see cref="ElementTheme"/> 而不是直接暴露 <see cref="AppTheme"/></b>：
/// 界面侧要的是「怎么设 <c>RequestedTheme</c>」这个答案，而 <see cref="AppTheme.System"/>
/// 对应的是 <see cref="ElementTheme.Default"/>（继承，即跟随系统）—— 这个映射不该散到 XAML 里。
/// </para>
/// </remarks>
public sealed partial class ThemeViewModel : ObservableObject
{
    private readonly IThemeSettingsStore _store;
    private bool _useAcrylicEffects;
    private StartupSection _startupPage;
    private TrayMenuStyle _trayMenuStyle;
    private ElementTheme _actualTheme = ElementTheme.Default;
    internal Func<Action, Task>? Transition { get; set; }

    public ThemeViewModel(IThemeSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        var settings = store.Load();
        Current = settings.Theme;
        _useAcrylicEffects = settings.AcrylicEffects ?? true;
        _startupPage = settings.StartupPage;
        _trayMenuStyle = settings.TrayMenuStyle;

        // 第一帧之前就定下来，避免启动时先画一帧亚克力再切成实色。
        AcrylicMaterial.SetEnabled(_useAcrylicEffects);
    }

    /// <summary>当前档位。</summary>
    public AppTheme Current { get; private set; }

    /// <summary>给 <c>RequestedTheme</c> 用。切换时会发通知，绑定方自己跟着变。</summary>
    public ElementTheme RequestedTheme => Current switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>
    /// 外壳上实际生效的浅色 / 深色。<b>已把「跟随系统」解析掉</b>，永远是具体值。
    /// </summary>
    /// <remarks>
    /// 托盘右键面板在托盘库自建的第二个窗口里，那个窗口的主题不会主动跟随外壳；
    /// 面板必须绑这个具体值，不能绑可能为 <see cref="ElementTheme.Default"/> 的
    /// <see cref="RequestedTheme"/> —— 否则「跟随系统」时会继承第二个窗口的旧主题。
    /// 由 <c>MainWindow</c> 在 <c>ShellRoot.ActualThemeChanged</c> 时更新。
    /// </remarks>
    public ElementTheme ActualTheme
    {
        get => _actualTheme;
        private set
        {
            if (_actualTheme == value)
            {
                return;
            }

            _actualTheme = value;
            OnPropertyChanged(nameof(ActualTheme));
        }
    }

    internal void UpdateActualTheme(ElementTheme theme) => ActualTheme = theme;

    /// <summary>当前档位的显示名。给按钮的提示文字用。</summary>
    public string CurrentLabel => Current switch
    {
        AppTheme.Light => "浅色",
        AppTheme.Dark => "深色",
        _ => "跟随系统",
    };

    /// <summary>标题栏当前主题图标键：显示器、太阳或月亮。</summary>
    public string CurrentIcon => Current switch
    {
        AppTheme.Light => "IconThemeLight",
        AppTheme.Dark => "IconThemeDark",
        _ => "IconThemeAuto",
    };

    public bool IsSystem => Current == AppTheme.System;

    public bool IsLight => Current == AppTheme.Light;

    public bool IsDark => Current == AppTheme.Dark;

    /// <summary>
    /// 托盘右键菜单的形态。改后立刻落盘；主窗口的菜单分组直接绑它。
    /// </summary>
    public TrayMenuStyle TrayMenu
    {
        get => _trayMenuStyle;
        set
        {
            if (_trayMenuStyle == value)
            {
                return;
            }

            _trayMenuStyle = value;
            Save();
            OnPropertyChanged(nameof(TrayMenu));
            OnPropertyChanged(nameof(TrayMenuStyleIndex));
            OnPropertyChanged(nameof(IsEfficientTrayMenu));
        }
    }

    /// <summary>给设置页下拉框用。下标顺序与 <see cref="TrayMenuStyle"/> 一致。</summary>
    public int TrayMenuStyleIndex
    {
        get => (int)TrayMenu;
        set
        {
            if (value is < 0 or > (int)TrayMenuStyle.Efficient)
            {
                return;
            }

            TrayMenu = (TrayMenuStyle)value;
        }
    }

    public bool IsEfficientTrayMenu => TrayMenu == TrayMenuStyle.Efficient;

    /// <summary>
    /// 应用内的磨砂表面是否使用亚克力。改后立刻生效并落盘。
    /// </summary>
    /// <remarks>
    /// 只影响应用自己定义的表面（弹层、对话框、抽屉、设置卡片与发现页/排行榜卡片）；
    /// 系统控件的下拉与托盘菜单仍跟随 Windows 的透明效果设置。
    /// </remarks>
    public bool UseAcrylicEffects
    {
        get => _useAcrylicEffects;
        set
        {
            if (_useAcrylicEffects == value)
            {
                return;
            }

            _useAcrylicEffects = value;
            Save();
            AcrylicMaterial.SetEnabled(value);
            OnPropertyChanged(nameof(UseAcrylicEffects));
        }
    }

    /// <summary>
    /// 启动后进入的侧栏根页。改后立刻落盘，下次启动生效。
    /// </summary>
    /// <remarks>
    /// 只在启动时读：改它不会把用户从当前页面拽走。未登录时账号专属页由外壳回退到发现页。
    /// </remarks>
    public StartupSection StartupPage
    {
        get => _startupPage;
        set
        {
            if (_startupPage == value)
            {
                return;
            }

            _startupPage = value;
            Save();
            OnPropertyChanged(nameof(StartupPage));
        }
    }

    /// <summary>
    /// 把当前所有外观字段一起写回。<b>不要在各处零星 new</b>：漏一个字段就会把它静默重置。
    /// </summary>
    private void Save() =>
        _store.Save(new ThemeSettings(Current, _useAcrylicEffects, _startupPage, _trayMenuStyle));

    /// <summary>
    /// 切到某个档位并立刻落盘。
    /// </summary>
    /// <remarks>
    /// 落盘是「改了立刻写」而不是退出时统一写：设置项就这一个，攒着没有收益，
    /// 反而多一条「进程被杀就丢设置」的路径。写失败由 store 自己吞掉并记日志。
    /// </remarks>
    public void Select(AppTheme theme)
    {
        if (Current == theme)
        {
            return;
        }

        Current = theme;
        Save();

        void NotifyTheme()
        {
            OnPropertyChanged(nameof(Current));
            OnPropertyChanged(nameof(RequestedTheme));
            OnPropertyChanged(nameof(CurrentLabel));
            OnPropertyChanged(nameof(CurrentIcon));
            OnPropertyChanged(nameof(IsSystem));
            OnPropertyChanged(nameof(IsLight));
            OnPropertyChanged(nameof(IsDark));
        }
        if (Transition is { } transition) _ = transition(NotifyTheme);
        else NotifyTheme();
    }
}
