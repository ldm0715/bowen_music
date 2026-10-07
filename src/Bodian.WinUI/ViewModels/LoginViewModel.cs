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

    /// <summary>
    /// 正在获取二维码，或已扫码、正在换取会话。
    /// </summary>
    /// <remarks>
    /// <b>不含「等着用户扫码」那一段。</b> 那个标题会驱动弹窗里的 <c>ProgressRing</c>，
    /// 挂在整段轮询上就会变成「二维码已经显示出来了，圈还在码上面转个不停」，
    /// 看着像一直没加载完 —— 而实际上用户这时该做的就是去扫码。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsWaiting { get; set; }

    /// <summary>
    /// 可以点「刷新二维码」。
    /// </summary>
    /// <remarks>
    /// 二维码一显示出来就置 <c>true</c>，而不是等轮询结束。否则那 5 分钟里
    /// 用户既看着圈转、又点不动这颗按钮，没有任何自救手段。
    /// </remarks>
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

    /// <remarks>
    /// <b>允许重入</b>：默认的 <c>AsyncRelayCommand</c> 在执行期间会把按钮禁掉，
    /// 而本命令的执行期等于整段轮询（最长 5 分钟）——那正好又堵死了「重新取一张码」。
    /// 重入是安全的：<see cref="StartAsync"/> 一进来就先 <see cref="StopPolling"/> 掐掉上一轮，
    /// 而 <c>finally</c> 里的判等保证各自只清自己那份。
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
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

            // 码已经出来了：停转圈、放开采刷新，剩下的时间交还给用户去扫码。
            IsWaiting = false;
            CanRefresh = true;

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

            // 判等只管「字段还是不是自己那次」——更新一轮已经开始了就别去清它。
            // 但 cts 是本方法独占的，成功分支会先调 StopPolling() 把它从字段上摘掉，
            // 所以释放不能跟着判等走，否则每成功登录一次漏一个 CancellationTokenSource。
            if (ReferenceEquals(_polling, cts))
            {
                _polling = null;
            }

            cts.Dispose();
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
                    // 从这里开始又是「等服务端」，把圈转回来并锁住刷新 —— 这次是真的在加载。
                    IsWaiting = true;
                    CanRefresh = false;
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
