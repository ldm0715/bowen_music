using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Bodian.WinUI.Converters;

/// <summary>
/// 布尔转可见性。需要「取反」时用另一个带 <see cref="Invert"/> 的实例。
/// </summary>
/// <remarks>
/// 之所以不用 <c>ConverterParameter</c>：<c>x:Bind</c> 对它的求值规则与 <c>Binding</c> 不同，
/// 用两个具名资源更省心，读起来也更直白。
/// </remarks>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>为 true 时把判断结果取反。</summary>
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is true;

        if (Invert)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("布尔转可见性是单向的。");
}
