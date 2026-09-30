using Bodian.Core.Api;
using Bodian.Core.Models.Login;
using Bodian.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 扫码登录页。
/// </summary>
/// <remarks>
/// 轮询节奏由这里掌握（<see cref="LoginOptions.PollInterval"/>，协议要求 ≥2 秒），
/// 而不是塞进 Core 的一个大循环里 —— 界面要能显示状态、要能随时取消、要能换一张码重来。
/// </remarks>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IBodianLogin _login;
    private readonly IQrImageFactory _qrImages;
    private readonly LoginOptions _options;
    private readonly ILogger<LoginViewModel> _logger;

    private CancellationTokenSource? _polling;

    public LoginViewModel(
        IBodianLogin login,
        IQrImageFactory qrImages,
        LoginOptions options,
        ILogger<LoginViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(qrImages);
        ArgumentNullException.ThrowIfNull(options);

        _login = login;
        _qrImages = qrImages;
        _options = options;
        _logger = logger ?? NullLogger<LoginViewModel>.Instance;
    }

    /// <summary>登录成功。宿主据此切到搜索页。</summary>
    public event EventHandler? LoggedIn;

    [ObservableProperty]
    public partial ImageSource? QrImage { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "准备中…";

    /// <summary>正在获取二维码或等待扫码。</summary>
    [ObservableProperty]
    public partial bool IsWaiting { get; set; }

    /// <summary>可以点「刷新二维码」。</summary>
    [ObservableProperty]
    public partial bool CanRefresh { get; set; } = true;

    /// <summary>页面加载时调用。已经登录过就直接放行，不打扰用户。</summary>
    public async Task ActivateAsync()
    {
        if (_login.IsAuthenticated)
        {
            LoggedIn?.Invoke(this, EventArgs.Empty);
            return;
        }

        await StartAsync();
    }

    /// <summary>页面卸载时调用，停掉轮询。</summary>
    public void Deactivate() => StopPolling();

    [RelayCommand]
    private Task RefreshAsync() => StartAsync();

    private async Task StartAsync()
    {
        StopPolling();

        var cts = new CancellationTokenSource();
        _polling = cts;

        IsWaiting = true;
        CanRefresh = false;
        QrImage = null;
        StatusText = "正在获取二维码…";

        try
        {
            var challenge = await _login.CreateChallengeAsync(cts.Token);

            // 渲染的是 LandingPage，不是 key —— 两者不是一回事。
            QrImage = _qrImages.Create(challenge.LandingPage.ToString());
            StatusText = "用波点 App 扫描二维码";

            await PollUntilDoneAsync(challenge, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // 页面卸载或用户刷新，正常。
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "扫码登录失败");
            StatusText = $"登录出错：{ex.Message}";
        }
        finally
        {
            IsWaiting = false;
            CanRefresh = true;

            if (ReferenceEquals(_polling, cts))
            {
                _polling = null;
                cts.Dispose();
            }
        }
    }

    private async Task PollUntilDoneAsync(QrCodeChallenge challenge, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + QrCodeChallenge.Lifetime;

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(_options.PollInterval, cancellationToken);

            switch (await _login.PollAsync(challenge, cancellationToken))
            {
                case QrScanStatus.Waiting:
                case QrScanStatus.Unknown:
                    continue;

                case QrScanStatus.Expired:
                    StatusText = "二维码已过期，请点「刷新二维码」";
                    return;

                case QrScanStatus.Confirmed:
                    StatusText = "已扫码，正在登录…";
                    await CompleteAsync(challenge, cancellationToken);
                    return;
            }
        }

        StatusText = "等待超时，请点「刷新二维码」";
    }

    private async Task CompleteAsync(QrCodeChallenge challenge, CancellationToken cancellationToken)
    {
        var outcome = await _login.CompleteAsync(challenge, cancellationToken);

        switch (outcome)
        {
            case LoginOutcome.Success success:
                StatusText = $"登录成功：{success.Nickname ?? success.Uid}";
                StopPolling();
                LoggedIn?.Invoke(this, EventArgs.Empty);
                break;

            case LoginOutcome.Expired:
                StatusText = "二维码已过期，请点「刷新二维码」";
                break;

            case LoginOutcome.IdentityMismatch:
                // 这是「拿到的会话可能不是你的」这种情况，必须让用户看到并重试。
                StatusText = "会话身份校验未通过，这次登录已放弃。请重新扫码。";
                break;

            case LoginOutcome.InvalidResponse invalid:
                StatusText = $"登录响应异常：{invalid.Detail}";
                break;

            case LoginOutcome.Failed failed:
                StatusText = $"登录失败（{failed.Code}）：{failed.Message ?? "服务端未说明原因"}";
                break;
        }
    }

    private void StopPolling()
    {
        var cts = _polling;
        _polling = null;

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经释放过了。
        }
    }
}
