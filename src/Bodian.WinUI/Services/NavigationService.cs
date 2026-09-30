using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Services;

/// <inheritdoc cref="INavigationService" />
public sealed class NavigationService(IServiceProvider services) : INavigationService
{
    private Frame? _frame;

    public void Attach(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
    }

    public void Navigate<TPage>() where TPage : Page => Show<TPage>();

    public void Reset<TPage>() where TPage : Page => Show<TPage>();

    private void Show<TPage>() where TPage : Page
    {
        if (_frame is null)
        {
            // 抛明确的异常，而不是等到 NullReferenceException —— 这个错误信息直接指出该怎么修。
            throw new InvalidOperationException(
                $"导航到 {typeof(TPage).Name} 之前必须先调用 {nameof(Attach)}(Frame)。");
        }

        _frame.Content = services.GetRequiredService<TPage>();
    }
}
