using System.Diagnostics;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Bowen.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Globalization;

namespace Bowen.WinUI.ViewModels;

/// <summary>界面字体偏好。设置只落盘，应用重启后统一应用。</summary>
public sealed partial class FontSettingsViewModel : ObservableObject
{
    private readonly IFontSettingsStore _store;
    private readonly ILogger<FontSettingsViewModel> _logger;

    public FontSettingsViewModel(IFontSettingsStore store, ILogger<FontSettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
        CurrentFontFamily = store.Load().Normalized().FontFamily;

        var locales = BuildLocaleList();
        var (fonts, canonicalToLocalized, installedNames) = LoadFonts(locales);

        // 旧版本可能存的是 en-US 名字。换成本地化名，避免下拉里中英文各一条。
        if (!string.IsNullOrEmpty(CurrentFontFamily)
            && canonicalToLocalized.TryGetValue(CurrentFontFamily, out var localized)
            && !string.IsNullOrWhiteSpace(localized))
        {
            CurrentFontFamily = localized;
        }

        // 在 DI 首次解析本服务时执行；组合根保证它发生在主窗口创建之前。
        if (!string.IsNullOrEmpty(CurrentFontFamily))
            Application.Current.Resources["ContentControlThemeFontFamily"] = new FontFamily(CurrentFontFamily);

        // 存档里的字体本机已不存在时，也让它出现在下拉里，而不是悄悄退回「默认字体」。
        if (!string.IsNullOrEmpty(CurrentFontFamily) && !installedNames.Contains(CurrentFontFamily))
        {
            fonts.Add(new FontOption(CurrentFontFamily, CurrentFontFamily, CurrentFontFamily, FontGroup.Missing));
            fonts = [.. fonts
                .OrderBy(option => option.Group)
                .ThenBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
        }

        Fonts = fonts;

        var posterOptions = new List<FontOption>
        {
            new(string.Empty, string.Empty, "跟随界面字体", FontGroup.Default),
        };
        posterOptions.AddRange(fonts);
        PosterFontOptions = posterOptions;

        _logger.LogInformation(
            "字体列表：{Count} 个（中文 {Chinese}，西文 {Western}，符号 {Symbol}，未安装 {Missing}），当前 {Current}",
            Fonts.Count,
            Fonts.Count(option => option.Group == FontGroup.Chinese),
            Fonts.Count(option => option.Group == FontGroup.Western),
            Fonts.Count(option => option.Group == FontGroup.Symbol),
            Fonts.Count(option => option.Group == FontGroup.Missing),
            CurrentFontFamily);
    }

    /// <summary>已保存的字体族名；空串 = 跟随系统默认。</summary>
    public string CurrentFontFamily { get; private set; }

    /// <summary>当前区域下可选的字体，已按分组和名称排序，不含「默认字体」。</summary>
    public IReadOnlyList<FontOption> Fonts { get; }

    /// <summary>海报弹窗里的字体选项：首项「跟随界面字体」，其余与 <see cref="Fonts"/> 一致。</summary>
    public IReadOnlyList<FontOption> PosterFontOptions { get; }

    /// <summary>
    /// 保存下次启动使用的字体，不改当前窗口。返回是否写入成功。
    /// </summary>
    /// <remarks>
    /// <b>保存后发一条重启提示</b>：改动对当前窗口完全不可见，用户会以为没保存上。
    /// 通知走 <see cref="INoticeSink"/>，注入方式与其它动作入口一致（见 App 组合根）。
    /// 写入失败时不更新已保存的选择，也不报成功。
    /// </remarks>
    public bool Select(string fontFamily)
    {
        var normalized = new FontSettings { FontFamily = fontFamily }.Normalized().FontFamily;
        if (string.Equals(CurrentFontFamily, normalized, StringComparison.Ordinal)) return true;

        if (!_store.Save(new FontSettings { FontFamily = normalized }))
        {
            _notice?.Show("界面字体未能保存，重启后不会生效", NoticeSeverity.Error);
            return false;
        }

        var isDefault = string.IsNullOrEmpty(normalized);
        CurrentFontFamily = normalized;
        OnPropertyChanged(nameof(CurrentFontFamily));

        _notice?.Show(isDefault
            ? "界面字体已恢复默认，重启应用后生效"
            : $"界面字体已保存为「{normalized}」，重启应用后生效", NoticeSeverity.Success);
        return true;
    }

    /// <summary>
    /// 通知出口。构造函数注入会形成循环（通知条依赖主窗口、主窗口构造依赖字体已应用），
    /// 所以由组合根在主窗口创建后回填 —— 与 <see cref="ThemeViewModel.Transition"/> 同一条接线思路。
    /// </summary>
    internal INoticeSink? Notice { get => _notice; set => _notice = value; }
    private INoticeSink? _notice;

    private static List<string> BuildLocaleList()
    {
        var locales = new List<string> { "zh-CN", "zh-Hans-CN", "zh-Hans" };
        foreach (var locale in ApplicationLanguages.Languages)
        {
            if (!locales.Any(item => string.Equals(item, locale, StringComparison.OrdinalIgnoreCase)))
                locales.Add(locale);
        }

        if (!locales.Any(item => string.Equals(item, "en-US", StringComparison.OrdinalIgnoreCase)))
            locales.Add("en-US");
        return locales;
    }

    private (List<FontOption> Fonts, Dictionary<string, string> CanonicalToLocalized, HashSet<string> InstalledNames)
        LoadFonts(IReadOnlyList<string> locales)
    {
        var stopwatch = Stopwatch.StartNew();
        var byCanonical = new Dictionary<string, FontAccumulator>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var fontSet = CanvasFontSet.GetSystemFontSet();
            foreach (var face in fontSet.Fonts)
            {
                var names = face.FamilyNames;
                if (names.Count == 0) continue;

                var displayName = PickName(names, locales);
                if (string.IsNullOrWhiteSpace(displayName)) continue;

                var canonicalName = PickName(names, ["en-US"]);
                if (string.IsNullOrWhiteSpace(canonicalName)) canonicalName = displayName;

                if (!byCanonical.TryGetValue(canonicalName, out var accumulator))
                {
                    accumulator = new FontAccumulator(displayName, canonicalName);
                    byCanonical.Add(canonicalName, accumulator);
                }

                accumulator.HasChinese |= face.HasCharacter(0x4E2Du) && face.HasCharacter(0x6C49u);
                accumulator.HasLatin |= face.HasCharacter(0x41u);
                accumulator.IsSymbol |= face.IsSymbolFont;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "系统字体集枚举失败，退回按家族名分类");
            return LoadFontsByName(locales);
        }

        var fonts = byCanonical.Values
            .Select(accumulator => accumulator.ToOption())
            .OrderBy(option => option.Group)
            .ThenBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var canonicalToLocalized = byCanonical.Values
            .ToDictionary(accumulator => accumulator.CanonicalName, accumulator => accumulator.DisplayName,
                StringComparer.OrdinalIgnoreCase);
        var installedNames = fonts
            .Select(option => option.FamilyName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        stopwatch.Stop();
        _logger.LogInformation("字体枚举：{Count} 个，用时 {Elapsed} ms", fonts.Count, stopwatch.ElapsedMilliseconds);
        return (fonts, canonicalToLocalized, installedNames);
    }

    private (List<FontOption> Fonts, Dictionary<string, string> CanonicalToLocalized, HashSet<string> InstalledNames)
        LoadFontsByName(IReadOnlyList<string> locales)
    {
        var names = CanvasTextFormat.GetSystemFontFamilies(locales)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var fonts = names
            .Select(name => new FontOption(name, name, name,
                name.Any(IsCjk) ? FontGroup.Chinese : FontGroup.Western))
            .OrderBy(option => option.Group)
            .ThenBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var canonicalToLocalized = names.ToDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase);
        var installedNames = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (fonts, canonicalToLocalized, installedNames);
    }

    private static string PickName(IReadOnlyDictionary<string, string> names, IReadOnlyList<string> locales)
    {
        foreach (var locale in locales)
        {
            foreach (var (key, value) in names)
            {
                if (string.Equals(key, locale, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return names.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static bool IsCjk(char c) => c >= 0x4E00 && c <= 0x9FFF;

    private sealed class FontAccumulator(string displayName, string canonicalName)
    {
        public string DisplayName { get; } = displayName;
        public string CanonicalName { get; } = canonicalName;
        public bool HasChinese { get; set; }
        public bool HasLatin { get; set; }
        public bool IsSymbol { get; set; }

        public FontOption ToOption() => new(
            DisplayName,
            CanonicalName,
            DisplayName,
            HasChinese ? FontGroup.Chinese
            : IsSymbol ? FontGroup.Symbol
            : HasLatin ? FontGroup.Western
            : FontGroup.Symbol);
    }
}
