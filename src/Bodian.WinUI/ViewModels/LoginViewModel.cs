using Bodian.Core.Api;
using Bodian.Core.Models.Login;
using Bodian.Core.Services;
using Bodian.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 登录页。两个页签：扫码与手机号。
/// </summary>
/// <remarks>
/// <para>
/// 扫码的轮询节奏由这里掌握（<see cref="LoginOptions.PollInterval"/>，协议要求 ≥2 秒），
/// 而不是塞进 Core 的一个大循环里 —— 界面要能显示状态、要能随时取消、要能换一张码重来。
/// </para>
/// <para>
/// <b>两条路径的出口只有一个</b>：<see cref="DescribeOutcome"/>，成功时同样只在这里触发
/// <see cref="LoggedIn"/>。宿主因此不必知道用户是用哪种方式登录的。
/// </para>
/// </remarks>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IBodianLogin _login;
    private readonly IQrImageFactory _qrImages;
    private readonly LoginOptions _options;
    private readonly ILogger<LoginViewModel> _logger;

    private CancellationTokenSource? _polling;
    private CancellationTokenSource? _countdown;

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

    // ── 页签 ────────────────────────────────────────────────────────────────

    /// <summary>页签名，与 <see cref="SelectedMethod"/> 一一对应。</summary>
    public IReadOnlyList<string> LoginMethods { get; } = ["扫码登录", "手机号登录"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQrMethod))]
    [NotifyPropertyChangedFor(nameof(IsPhoneMethod))]
    public partial int SelectedMethod { get; set; }

    public bool IsQrMethod => SelectedMethod == 0;

    public bool IsPhoneMethod => SelectedMethod == 1;

    /// <summary>
    /// 切页签时要掐掉另一条路径的后台动作。
    /// </summary>
    /// <remarks>
    /// <b>切走就必须停轮询</b>，否则用户在输手机号，后台还在每 2 秒打一次 <c>qrCodeStatus</c>，
    /// 最坏持续 5 分钟。切回扫码则重新取一张码 —— <see cref="StartAsync"/> 一进来就先
    /// <see cref="StopPolling"/>，重入是安全的。
    /// </remarks>
    partial void OnSelectedMethodChanged(int value)
    {
        if (value == 0)
        {
            _ = StartAsync();
        }
        else
        {
            StopPolling();
        }
    }

    // ── 扫码 ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial ImageSource? QrImage { get; set; }

    /// <summary>
    /// 扫码那一路的状态 / 错误文字。
    /// </summary>
    /// <remarks>
    /// <b>两条路径各存一份，不共用。</b> 两个面板同时留在可视树上（只切 <c>Visibility</c>），
    /// 共用一条文字就会出现「切到手机号登录，下面还写着『用波点 App 扫描二维码』」；
    /// 而且扫码的轮询在切走后仍可能把文字写回来，覆盖掉手机号那一边的提示。
    /// </remarks>
    [ObservableProperty]
    public partial string QrStatusText { get; set; } = "准备中…";

    /// <summary>手机号那一路的状态 / 错误文字。</summary>
    [ObservableProperty]
    public partial string PhoneStatusText { get; set; } = "";

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

    // ── 手机号 ──────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRequestCode))]
    [NotifyPropertyChangedFor(nameof(CanSubmitPhone))]
    public partial string Phone { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitPhone))]
    public partial string VerifyCode { get; set; } = "";

    /// <summary>
    /// 手机号这一路正在等网络。**与 <see cref="IsWaiting"/> 分开**：那个驱动扫码二维码上的
    /// 转圈，两个页签的内容都在可视树上，共用一个标志会让另一个页签也转起来。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRequestCode))]
    [NotifyPropertyChangedFor(nameof(CanSubmitPhone))]
    public partial bool IsPhoneBusy { get; set; }

    /// <summary>重发倒计时剩余秒数。0 表示可以发。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodeButtonText))]
    [NotifyPropertyChangedFor(nameof(CanRequestCode))]
    public partial int ResendSeconds { get; set; }

    public string CodeButtonText => ResendSeconds > 0 ? $"{ResendSeconds} 秒后重发" : "获取验证码";

    /// <remarks>
    /// 格式在本地判一次只是省一次网络与一次风控计数，判断口径见
    /// <see cref="MobileNumber"/>；服务端仍会自己判一遍。
    /// </remarks>
    public bool CanRequestCode => !IsPhoneBusy && ResendSeconds == 0 && MobileNumber.IsValid(Phone);

    public bool CanSubmitPhone =>
        !IsPhoneBusy && MobileNumber.IsValid(Phone) && VerifyCode.Trim().Length is >= 4 and <= 6;

    // ── 生命周期 ────────────────────────────────────────────────────────────

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

    /// <summary>页面卸载时调用：两条路径的后台动作都要停。</summary>
    public void Deactivate()
    {
        StopPolling();
        StopCountdown();
    }

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
        QrStatusText = "正在获取二维码…";

        try
        {
            var challenge = await _login.CreateChallengeAsync(cts.Token);

            // 渲染的是 LandingPage，不是 key —— 两者不是一回事。
            QrImage = _qrImages.Create(challenge.LandingPage.ToString());
            QrStatusText = "用波点 App 扫描二维码";

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
            QrStatusText = $"登录出错：{ex.Message}";
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
                    QrStatusText = "二维码已过期，请点「刷新二维码」";
                    return;

                case QrScanStatus.Confirmed:
                    // 从这里开始又是「等服务端」，把圈转回来并锁住刷新 —— 这次是真的在加载。
                    IsWaiting = true;
                    CanRefresh = false;
                    QrStatusText = "已扫码，正在登录…";
                    QrStatusText = DescribeOutcome(await _login.CompleteAsync(challenge, cancellationToken));
                    return;
            }
        }

        QrStatusText = "等待超时，请点「刷新二维码」";
    }

    // ── 手机号流程 ──────────────────────────────────────────────────────────

    /// <remarks>
    /// <b>不 <c>ConfigureAwait(false)</c></b>：下面的属性被 <c>x:Bind</c> 绑着，
    /// 续体跑回线程池会抛 <c>0x8001010E</c>。本文件里所有 <c>await</c> 都遵循这条。
    /// </remarks>
    [RelayCommand]
    private async Task RequestCodeAsync()
    {
        if (MobileNumber.Normalize(Phone) is not { } mobile)
        {
            PhoneStatusText = "手机号格式不正确。";
            return;
        }

        IsPhoneBusy = true;
        PhoneStatusText = "正在发送验证码…";

        try
        {
            switch (await _login.SendSmsCodeAsync(mobile))
            {
                case SmsSendOutcome.Sent:
                    PhoneStatusText = "验证码已发送，请查收短信。";
                    StartCountdown();
                    break;

                case SmsSendOutcome.Failed failed:
                    PhoneStatusText = $"验证码发送失败（{failed.Code}）：{failed.Message ?? "服务端未说明原因"}";
                    break;
            }
        }
        catch (Exception ex)
        {
            // 不记手机号。
            _logger.LogWarning(ex, "发送短信验证码失败");
            PhoneStatusText = $"发送验证码出错：{ex.Message}";
        }
        finally
        {
            IsPhoneBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoginByPhoneAsync()
    {
        if (MobileNumber.Normalize(Phone) is not { } mobile)
        {
            PhoneStatusText = "手机号格式不正确。";
            return;
        }

        IsPhoneBusy = true;
        PhoneStatusText = "正在登录…";

        try
        {
            PhoneStatusText = DescribeOutcome(await _login.LoginByPhoneAsync(mobile, VerifyCode.Trim()));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "手机号登录失败");
            PhoneStatusText = $"登录出错：{ex.Message}";
        }
        finally
        {
            IsPhoneBusy = false;
        }
    }

    /// <summary>
    /// 重发冷却。
    /// </summary>
    /// <remarks>
    /// 用 <c>await Task.Delay</c> 的续体回 UI 线程，而不是 <c>Task.Run</c> + 调度器 ——
    /// 本方法是从 UI 线程起的，续体自然落回原上下文，和 <see cref="PollUntilDoneAsync"/> 同一套路。
    /// <b>绝不能与扫码轮询共用那个 CTS</b>：切页签会互相掐死。
    /// </remarks>
    private void StartCountdown()
    {
        StopCountdown();

        var cts = new CancellationTokenSource();
        _countdown = cts;

        _ = RunCountdownAsync(cts);
    }

    private async Task RunCountdownAsync(CancellationTokenSource cts)
    {
        try
        {
            for (var remaining = (int)_options.SmsResendInterval.TotalSeconds;
                 remaining > 0 && !cts.IsCancellationRequested;
                 remaining--)
            {
                ResendSeconds = remaining;
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 页面卸载、切页签、或又发了一次，正常。
        }
        finally
        {
            // 只有还轮得到自己时才归零：新一轮倒计时已经把字段换掉了，
            // 这时去写 0 会把刚设好的秒数抹掉。
            if (ReferenceEquals(_countdown, cts))
            {
                _countdown = null;
                ResendSeconds = 0;
            }

            cts.Dispose();
        }
    }

    // ── 结果分派 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 两条路径共用的出口。<b>成功时只有这里会触发 <see cref="LoggedIn"/>。</b>
    /// </summary>
    /// <returns>给界面看的状态文字。</returns>
    /// <remarks>
    /// <b>返回文字而不是直接写属性</b>：两个页签各有一条状态行，
    /// 由调用方决定写给哪一条。这里自己写就会把结果串到另一条路径的提示上。
    /// </remarks>
    private string DescribeOutcome(LoginOutcome outcome)
    {
        switch (outcome)
        {
            case LoginOutcome.Success success:
                StopPolling();
                StopCountdown();
                LoggedIn?.Invoke(this, EventArgs.Empty);
                return $"登录成功：{success.Nickname ?? success.Uid}";

            case LoginOutcome.Expired:
                return "二维码已过期，请点「刷新二维码」";

            case LoginOutcome.IdentityMismatch:
                // 这是「拿到的会话可能不是你的」这种情况，必须让用户看到并重试。
                return "会话身份校验未通过，这次登录已放弃。请重新登录。";

            case LoginOutcome.InvalidResponse invalid:
                return $"登录响应异常：{invalid.Detail}";

            case LoginOutcome.Failed failed:
                return $"登录失败（{failed.Code}）：{failed.Message ?? "服务端未说明原因"}";

            default:
                return "";
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

        Cancel(cts);
    }

    private void StopCountdown()
    {
        var cts = _countdown;
        _countdown = null;

        if (cts is null)
        {
            return;
        }

        Cancel(cts);
        ResendSeconds = 0;
    }

    private static void Cancel(CancellationTokenSource cts)
    {
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
