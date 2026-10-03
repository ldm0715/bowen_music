using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 列表末尾的状态提示：「没有更多了哦~」或「加载失败 + 重试」。
/// </summary>
/// <remarks>
/// <para>
/// <b>放进列表的 <c>Footer</c>，不是页脚。</b> 钉在页面底部的话，提示与列表末尾之间
/// 会隔着一大片空白，看不出「是这个列表到底了」。
/// </para>
/// <para>
/// 文案与版式收在这里而不是让十来个列表各写一遍：各处说法完全一样，没有理由重复。
/// </para>
/// </remarks>
public sealed partial class PagingEndNote : UserControl
{
    /// <summary>已经取完。显示「没有更多了哦~」。</summary>
    public static readonly DependencyProperty ShowEndProperty =
        DependencyProperty.Register(
            nameof(ShowEnd),
            typeof(bool),
            typeof(PagingEndNote),
            new PropertyMetadata(false));

    /// <summary>翻页失败且还有下一页。显示「加载失败」与重试按钮。</summary>
    public static readonly DependencyProperty ShowRetryProperty =
        DependencyProperty.Register(
            nameof(ShowRetry),
            typeof(bool),
            typeof(PagingEndNote),
            new PropertyMetadata(false));

    /// <summary>重试要执行的命令，通常是列表的 <c>LoadMoreCommand</c>。</summary>
    public static readonly DependencyProperty RetryCommandProperty =
        DependencyProperty.Register(
            nameof(RetryCommand),
            typeof(ICommand),
            typeof(PagingEndNote),
            new PropertyMetadata(null));

    public PagingEndNote()
    {
        InitializeComponent();
    }

    public bool ShowEnd
    {
        get => (bool)GetValue(ShowEndProperty);
        set => SetValue(ShowEndProperty, value);
    }

    public bool ShowRetry
    {
        get => (bool)GetValue(ShowRetryProperty);
        set => SetValue(ShowRetryProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => (ICommand?)GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }
}
