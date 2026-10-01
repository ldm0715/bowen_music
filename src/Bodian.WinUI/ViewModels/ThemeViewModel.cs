using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace Bodian.WinUI.ViewModels;

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

    public ThemeViewModel(IThemeSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        Current = store.Load().Theme;
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

    /// <summary>当前档位的显示名。给按钮的提示文字用。</summary>
    public string CurrentLabel => Current switch
    {
        AppTheme.Light => "浅色",
        AppTheme.Dark => "深色",
        _ => "跟随系统",
    };

    public bool IsSystem => Current == AppTheme.System;

    public bool IsLight => Current == AppTheme.Light;

    public bool IsDark => Current == AppTheme.Dark;

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
        _store.Save(new ThemeSettings(theme));

        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(RequestedTheme));
        OnPropertyChanged(nameof(CurrentLabel));
        OnPropertyChanged(nameof(IsSystem));
        OnPropertyChanged(nameof(IsLight));
        OnPropertyChanged(nameof(IsDark));
    }
}
