using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 扫码登录页。
/// </summary>
/// <remarks>
/// 页面激活走 <see cref="FrameworkElement.Loaded"/> 而不是 <c>OnNavigatedTo</c> ——
/// 导航是直接给 <c>Frame.Content</c> 赋实例（为了保留构造函数注入），不会触发导航事件。
/// </remarks>
public sealed partial class LoginPage : Page
{
    private readonly INavigationService _navigation;

    public LoginPage(LoginViewModel viewModel, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        ViewModel = viewModel;
        _navigation = navigation;

        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public LoginViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.LoggedIn += OnLoggedIn;
        await ViewModel.ActivateAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.LoggedIn -= OnLoggedIn;
        ViewModel.Deactivate();
    }

    /// <summary>登录成功。用 Reset 而不是 Navigate：登录页不该还能退回去。</summary>
    private void OnLoggedIn(object? sender, EventArgs e) => _navigation.Reset<SearchPage>();
}
