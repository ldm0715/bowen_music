using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Bowen.WinUI.Services;

/// <summary>
/// 应用自己的亚克力刷：所有磨砂表面共用同一个实例，设置里可以一键退回实色。
/// </summary>
/// <remarks>
/// <para>
/// 用自建刷而不是系统 <c>AcrylicBackgroundFillColorDefaultBrush</c>，是因为后者分散在
/// 框架的主题字典里，运行期拿不到具体实例，无法切 <see cref="AcrylicBrush.AlwaysUseFallback"/>。
/// 这里自己持有 Light / Dark 两个刷子，切换只改一个属性，所有引用同刷子的表面一起变。
/// </para>
/// <para>
/// 浅色 / 深色各给一层明显的 tint 与亮度，保证大面积卡片在氛围渐变上仍然看得出边界
/// （系统 Default 的浅色 TintOpacity 是 0，只适合临时弹层）。兜底色与改动前的实色卡片一致。
/// 高对比度用系统背景色实色，与系统保持一致的成对前景 / 背景。
/// </para>
/// <para>
/// 只覆盖应用自己的表面。系统控件的下拉、托盘菜单仍走框架默认刷，跟随 Windows 的透明效果设置。
/// </para>
/// </remarks>
public static class AcrylicMaterial
{
    /// <summary>XAML 里引用的资源键。</summary>
    public const string BrushKey = "BowenAcrylicBrush";

    /// <summary>下拉弹层共用的表面刷：ComboBox 弹层与字体选择器 Flyout 都引用它。</summary>
    public const string PopupBrushKey = "BowenPopupSurfaceBrush";

    private static AcrylicBrush? _light;
    private static AcrylicBrush? _dark;
    private static SolidColorBrush? _popupLight;
    private static SolidColorBrush? _popupDark;
    private static SolidColorBrush? _highContrast;
    private static bool _enabled = true;

    /// <summary>
    /// 注入主题字典。必须在创建任何窗口之前调用（<see cref="App.OnLaunched"/> 里）。
    /// 不能放在 App 构造函数里：那时 <c>Application.Resources</c> 还取不到，
    /// 会抛 COMException 0x8000FFFF 导致启动即崩。
    /// </summary>
    public static void Initialize()
    {
        if (_light is not null)
        {
            return;
        }

        // 系统浅色默认的 TintOpacity 是 0（只模糊、不着色），那是按临时弹层调的；
        // 放在大面积卡片上会与页面背景糊成一片，所以这里显式给 tint 与亮度层。
        _light = new AcrylicBrush
        {
            TintColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            TintOpacity = 0.35,
            TintLuminosityOpacity = 0.6,
            FallbackColor = Color.FromArgb(0xFF, 0xFA, 0xFA, 0xFA),
        };

        _dark = new AcrylicBrush
        {
            TintColor = Color.FromArgb(0xFF, 0x20, 0x20, 0x20),
            TintOpacity = 0.4,
            TintLuminosityOpacity = 0.35,
            FallbackColor = Color.FromArgb(0xFF, 0x20, 0x20, 0x20),
        };

        // 弹层是独立合成层，亚克力没有页面内容可采样，统一用半透明表面色。
        // ComboBox 弹层与字体选择器 Flyout 共用这一份，样式才对得齐。
        _popupLight = new SolidColorBrush(Color.FromArgb(0xEB, 0xF3, 0xF3, 0xF3));
        _popupDark = new SolidColorBrush(Color.FromArgb(0xEB, 0x1C, 0x1C, 0x1C));

        var highContrast = new SolidColorBrush(
            new UISettings().GetColorValue(UIColorType.Background));
        _highContrast = highContrast;

        var lightTheme = new ResourceDictionary();
        lightTheme[BrushKey] = _light;
        lightTheme[PopupBrushKey] = _popupLight;

        var darkTheme = new ResourceDictionary();
        darkTheme[BrushKey] = _dark;
        darkTheme[PopupBrushKey] = _popupDark;

        var highContrastTheme = new ResourceDictionary();
        highContrastTheme[BrushKey] = highContrast;
        highContrastTheme[PopupBrushKey] = highContrast;

        var dictionary = new ResourceDictionary();
        dictionary.ThemeDictionaries["Light"] = lightTheme;
        dictionary.ThemeDictionaries["Dark"] = darkTheme;
        dictionary.ThemeDictionaries["HighContrast"] = highContrastTheme;

        Application.Current.Resources.MergedDictionaries.Add(dictionary);
    }

    /// <summary>
    /// 开启时走真实模糊；关闭时两个刷子都强制使用各自的 FallbackColor。
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        _enabled = enabled;

        if (_light is not null)
        {
            _light.AlwaysUseFallback = !enabled;
        }

        if (_dark is not null)
        {
            _dark.AlwaysUseFallback = !enabled;
        }

        // 开关亚克力时，弹层表面也要在「半透明」与「实色」之间切。
        var alpha = enabled ? (byte)0xEB : (byte)0xFF;
        if (_popupLight is not null)
        {
            _popupLight.Color = Color.FromArgb(alpha, 0xF3, 0xF3, 0xF3);
        }

        if (_popupDark is not null)
        {
            _popupDark.Color = Color.FromArgb(alpha, 0x1C, 0x1C, 0x1C);
        }
    }

    /// <summary>
    /// 弹层使用的表面色。
    /// </summary>
    /// <remarks>
    /// <b>弹层里不能用 <see cref="AcrylicBrush"/>。</b> Popup 是独立合成层，亚克力没有
    /// 可以采样的页面内容，只剩 tint 叠在黑底上，观感接近纯黑。这里改用半透明表面色，
    /// 让弹层透出下面的卡片或壁纸：开启亚克力时 70% 不透明，关闭时完全不透明。
    /// 高对比度仍用系统背景实色。
    /// </remarks>
    public static Brush GetPopupBrush(ElementTheme theme)
    {
        try
        {
            if (new AccessibilitySettings().HighContrast && _highContrast is not null)
            {
                return _highContrast;
            }
        }
        catch
        {
            // 高对比度查询失败时按普通主题处理。
        }

        // 返回共享实例：XAML 里的 BowenPopupSurfaceBrush 与这里指向同一份，
        // 主题 / 亚克力开关变化时两边一起更新。
        if (theme == ElementTheme.Dark)
        {
            return _popupDark ?? new SolidColorBrush(Color.FromArgb(0xEB, 0x1C, 0x1C, 0x1C));
        }

        return _popupLight ?? new SolidColorBrush(Color.FromArgb(0xEB, 0xF3, 0xF3, 0xF3));
    }
}
