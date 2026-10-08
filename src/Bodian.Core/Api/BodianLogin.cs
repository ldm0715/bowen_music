using System.Globalization;
using System.Text.Json;
using Bodian.Core.Api.Dto;
using Bodian.Core.Api.Dto.Requests;
using Bodian.Core.Models.Account;
using Bodian.Core.Models.Login;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Api;

/// <inheritdoc cref="IBodianLogin" />
public sealed class BodianLogin : IBodianLogin
{
    private readonly IBodianTransport _transport;
    private readonly BodianSession _session;
    private readonly ICredentialStore _credentials;
    private readonly TimeProvider _time;
    private readonly LoginOptions _options;
    private readonly ILogger<BodianLogin> _logger;

    public BodianLogin(
        IBodianTransport transport,
        BodianSession session,
        ICredentialStore credentials,
        LoginOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<BodianLogin>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(credentials);

        _transport = transport;
        _session = session;
        _credentials = credentials;
        _options = options ?? LoginOptions.Default;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<BodianLogin>.Instance;

        // 服务端说会话无效（11012）时，磁盘上的凭据也必须清掉——
        // 否则下次启动会恢复一个已经死掉的 token，表现为「一启动就掉登录」。
        _session.Cleared += OnSessionCleared;
    }

    public bool IsAuthenticated => _session.IsAuthenticated;

    public BodianAccount? Account { get; private set; }

    public string? Nickname => Account?.Nickname;

    public event EventHandler? AccountChanged;

    public async Task<QrCodeChallenge> CreateChallengeAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.LoginQrCode,
                Signed = true,
            },
            BodianJsonContext.Default.QrCodeDto,
            cancellationToken).ConfigureAwait(false);

        var key = envelope.Data?.QrCode;

        if (string.IsNullOrEmpty(key))
        {
            // 业务码 200 却没有 key，属于服务端契约被破坏，不是可降级的业务分支。
            throw new BodianApiException(
                BodianErrorCode.MalformedResponse,
                envelope.Code,
                Endpoints.LoginQrCode,
                "响应里没有 qrCode",
                envelope.RequestId);
        }

        return new QrCodeChallenge(key, QrLoginContent.ForKey(key), _time.GetUtcNow());
    }

    public async Task<QrScanStatus> PollAsync(
        QrCodeChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.LoginQrCodeStatus,
                Query = [new KeyValuePair<string, string>("qrCode", challenge.Key)],
                Signed = true,
                // 轮询期间服务端会返回 11027，它不是错误。
                AcceptedCodes = [BodianErrorCode.LoginPending],
            },
            BodianJsonContext.Default.QrCodeStatusDto,
            cancellationToken).ConfigureAwait(false);

        return envelope.Data?.Status switch
        {
            1 => QrScanStatus.Waiting,
            2 => QrScanStatus.Expired,
            3 => QrScanStatus.Confirmed,
            _ => QrScanStatus.Unknown,
        };
    }

    public async Task<LoginOutcome> CompleteAsync(
        QrCodeChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        var body = JsonSerializer.Serialize(
            new LoginBody { QrCode = challenge.Key },
            BodianJsonContext.Default.LoginBody);

        for (var attempt = 1; attempt <= _options.MaxExchangeAttempts; attempt++)
        {
            var envelope = await _transport.SendAsync(
                new BodianRequest
                {
                    Path = Endpoints.UsersLogin,
                    Verb = BodianHttpVerb.Post,
                    JsonBody = body,
                    Signed = true,
                    AcceptedCodes = [BodianErrorCode.LoginPending],
                },
                BodianJsonContext.Default.LoginResultDto,
                cancellationToken).ConfigureAwait(false);

            if (envelope.Code == (int)BodianErrorCode.LoginPending)
            {
                // 已扫码但用户还没点确认——等一下重试，不是失败。
                if (attempt < _options.MaxExchangeAttempts)
                {
                    await Task.Delay(_options.ExchangeRetryInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _logger.LogWarning("换取会话连续 {Attempts} 次返回 11027，放弃", attempt);
                return new LoginOutcome.Failed(envelope.Code, envelope.Message);
            }

            if (envelope.Code != (int)BodianErrorCode.Success || envelope.Data is null)
            {
                return new LoginOutcome.Failed(envelope.Code, envelope.Message);
            }

            return Adopt(envelope.Data);
        }

        return new LoginOutcome.Failed((int)BodianErrorCode.LoginPending, "换取会话重试次数用尽");
    }

    public async Task<SmsSendOutcome> SendSmsCodeAsync(
        string mobile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mobile);

        // ★ 这条是 GET + query，不是 POST body。做错方向会拿到 HTTP 500 而不是业务码，
        //   排查起来很费时间 —— 见 reverse/findings/16-phone-login.md 第 2 节。
        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.SendSms,
                Query =
                [
                    new KeyValuePair<string, string>(
                        "type",
                        PhoneLoginBody.LoginSmsType.ToString(CultureInfo.InvariantCulture)),
                    new KeyValuePair<string, string>("mobile", mobile),
                ],
                Signed = true,

                // 「短信发送失败」（号码无效一类）是正常业务结果，不是异常。
                // 其余非 200（402 / 439 / 11012）继续抛 BodianApiException —— 那些是链路问题。
                AcceptedCodes = [BodianErrorCode.SmsSendFailed],
            },
            BodianJsonContext.Default.JsonElement,
            cancellationToken).ConfigureAwait(false);

        if (envelope.Code == (int)BodianErrorCode.Success)
        {
            // 不记手机号。
            _logger.LogInformation("短信验证码已投递");
            return new SmsSendOutcome.Sent();
        }

        _logger.LogWarning("短信验证码发送失败，code={Code}", envelope.Code);
        return new SmsSendOutcome.Failed(envelope.Code, envelope.Message);
    }

    public async Task<LoginOutcome> LoginByPhoneAsync(
        string mobile,
        string verifyCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mobile);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifyCode);

        var body = JsonSerializer.Serialize(
            new PhoneLoginBody { Mobile = mobile, VerifyCode = verifyCode },
            BodianJsonContext.Default.PhoneLoginBody);

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.UsersLogin,
                Verb = BodianHttpVerb.Post,
                JsonBody = body,
                Signed = true,

                // 「验证码错误」是用户输错了，正常业务结果。
                // 注意**不**接受 11027：那是扫码轮询专有的中间态，本路径一次定生死。
                AcceptedCodes = [BodianErrorCode.VerifyCodeWrong],
            },
            BodianJsonContext.Default.LoginResultDto,
            cancellationToken).ConfigureAwait(false);

        if (envelope.Code != (int)BodianErrorCode.Success || envelope.Data is null)
        {
            return new LoginOutcome.Failed(envelope.Code, envelope.Message);
        }

        // 与扫码共用同一条落地路径：身份校验 → 先落盘 → 再改内存会话 → 通知界面。
        return Adopt(envelope.Data);
    }

    public bool TryRestorePersistedSession()
    {
        var credential = _credentials.Load();

        if (credential is not { IsAuthenticated: true })
        {
            return false;
        }

        _session.Set(credential.Uid, credential.Token);

        // VipBadge 是后加的字段：老凭据里没有它，读出来是 None。
        // 直接采用会让老会话一个图标都不显示，所以按「是会员但档位认不出来」处理 ——
        // 退回大会员，与 VipStatus.ResolveBadge 的回落口径一致。
        // 准确档位要等下一次登录才会写进凭据。
        var badge = credential.IsVip && credential.VipBadge == VipBadgeKind.None
            ? VipBadgeKind.Big
            : credential.VipBadge;

        Account = new BodianAccount(
            credential.Uid,
            credential.Nickname,
            HttpUrl.TryParse(credential.AvatarUrl),
            credential.IsVip,
            credential.VipExpiresAt,
            badge);

        RaiseAccountChanged();

        _logger.LogInformation("已从磁盘恢复会话，uid={Uid}", credential.Uid);

        return true;
    }

    public void SignOut()
    {
        // Clear() 会触发 Cleared，由 OnSessionCleared 统一收尾（清凭据 + 通知）。
        _session.Clear();
    }

    /// <summary>
    /// 校验身份并采用会话。
    /// </summary>
    /// <remarks>
    /// 身份字段实测只有 <c>id</c> / <c>bid</c> / <c>userInfo.id</c> 三个存在，
    /// 但校验走通用的 <see cref="SessionIdentity"/>：出现几个比几个，要求两两相等且至少两个。
    /// </remarks>
    private LoginOutcome Adopt(LoginResultDto data)
    {
        if (string.IsNullOrEmpty(data.Token))
        {
            _logger.LogWarning("登录响应里没有 token，丢弃该会话");
            return new LoginOutcome.InvalidResponse("响应里没有 token");
        }

        var uid = SessionIdentity.Resolve(data.Id, data.Bid, data.Uid, data.UserInfo?.Id, null);

        if (uid is null)
        {
            var observed = DescribeIdentities(data);
            _logger.LogWarning("身份校验不通过，丢弃该会话：{Observed}", observed);
            return new LoginOutcome.IdentityMismatch(observed);
        }

        var uidText = uid.Value.ToString(CultureInfo.InvariantCulture);
        var userInfo = data.UserInfo;
        var (isVip, vipExpiresAt) = VipStatus.Resolve(data.PayInfo);
        var vipBadge = VipStatus.ResolveBadge(data.PayInfo);

        var account = new BodianAccount(
            uidText,
            userInfo?.NickName,
            HttpUrl.TryParse(userInfo?.HeadImg),
            isVip,
            vipExpiresAt,
            vipBadge);

        // 先落盘再改内存会话：落盘失败宁可没有会话，也不要出现「内存里登录了、重启就没了」。
        // 头像与会员状态也一并存下来 —— 否则下次从磁盘恢复时界面就只剩一个昵称。
        _credentials.Save(new BodianCredential(
            uidText,
            data.Token,
            account.Nickname,
            account.Avatar?.ToString(),
            account.IsVip,
            account.VipExpiresAt,
            account.VipBadge));

        _session.Set(uidText, data.Token);
        Account = account;
        RaiseAccountChanged();

        _logger.LogInformation("登录成功，uid={Uid}，会员={IsVip}", uidText, isVip);

        return new LoginOutcome.Success(uidText, account.Nickname);
    }

    private void OnSessionCleared(object? sender, BodianSessionClearedEventArgs e)
    {
        _credentials.Clear();
        Account = null;
        RaiseAccountChanged();
    }

    private void RaiseAccountChanged() => AccountChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>把出现过的身份字段拼成诊断串。**不含 token。**</summary>
    private static string DescribeIdentities(LoginResultDto data)
    {
        static string Show(long? value) =>
            value?.ToString(CultureInfo.InvariantCulture) ?? "-";

        return string.Join(
            "、",
            $"id={Show(data.Id)}",
            $"bid={Show(data.Bid)}",
            $"uid={Show(data.Uid)}",
            $"userInfo.id={Show(data.UserInfo?.Id)}");
    }
}
