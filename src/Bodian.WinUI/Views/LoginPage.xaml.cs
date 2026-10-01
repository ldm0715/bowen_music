using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>登录宿主，在主窗口中央显示扫码登录对话框。</summary>
public sealed partial class LoginPage : Page
{
    private readonly INavigationService _navigation;
    private bool _isActive;
    private bool _canCloseDialog;

    public LoginPage(LoginViewModel viewModel, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(navigation);

        ViewModel = viewModel;
        _navigation = navigation;

        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => LoginDialog.RequestedTheme = ActualTheme;
    }

    public LoginViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        _canCloseDialog = false;
        ViewModel.LoggedIn += OnLoggedIn;

        LoginDialog.XamlRoot = XamlRoot;
        LoginDialog.RequestedTheme = ActualTheme;

        // 先显示对话框，再开始扫码；已恢复会话时的同步 LoggedIn 也能正常关闭它。
        var dialogOperation = LoginDialog.ShowAsync();
        await ViewModel.ActivateAsync();
        await dialogOperation;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isActive = false;
        _canCloseDialog = true;
        ViewModel.LoggedIn -= OnLoggedIn;
        ViewModel.Deactivate();
        LoginDialog.Hide();
    }

    private void OnDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // 现有外壳需要登录才能使用；Escape 不应留下没有登录入口的空页面。
        args.Cancel = !_canCloseDialog;
    }

    /// <summary>成功后关闭弹窗并进入“我喜欢的”，导航历史不保留登录宿主。</summary>
    private void OnLoggedIn(object? sender, EventArgs e)
    {
        _canCloseDialog = true;
        LoginDialog.Hide();
        _navigation.NavigateRoot<FavoritesPage>();
    }
}
