namespace Bodian.Core.Api;

/// <summary>
/// 业务响应码。响应恒为 JSON，顶层 <c>code</c> 字段。
/// </summary>
/// <remarks>
/// 实测值见 <c>docs/bodian-api-reference.md</c> 1.5 节。**未知码不要丢弃**——
/// <see cref="BodianEnvelope{T}.Code"/> 始终保留原始整数，这个枚举只是给常见分支用的。
/// </remarks>
public enum BodianErrorCode
{
    /// <summary>不认识的码。原始值在 <see cref="BodianEnvelope{T}.Code"/> 里。</summary>
    Unknown = -1,

    /// <summary>成功，数据在 <c>data</c>。</summary>
    Success = 200,

    /// <summary>缺少必要请求头。检查请求头表。</summary>
    MissingHeaders = 402,

    /// <summary>签名无效（<c>msg: "sign invalid"</c>）。见 <c>BodianSigner</c> 的版本门说明。</summary>
    SignInvalid = 439,

    /// <summary>需要鉴权。<b>必须清除本地会话。</b></summary>
    NeedAuth = 11012,

    /// <summary>
    /// 登录中间态：<c>ucenter/users/login</c> 上「已扫未确认」。**继续轮询，不算失败。**
    /// </summary>
    /// <remarks>
    /// 实测它比字面意思更泛：<c>authType=4</c>（账号密码）失败时回的也是它，文案「账号登录失败」。
    /// 扫码轮询那条路径的现有判读不动，只是别以为这个码专属于扫码。
    /// </remarks>
    LoginPending = 11027,

    /// <summary>
    /// 短信发送失败（<c>ucenter/code/sendsms</c>）。
    /// </summary>
    /// <remarks>
    /// <b>「号码无效」与「字段名不对」都会回它</b>，定性时不能只看这个码。
    /// 见 <c>reverse/findings/16-phone-login.md</c> 第 5 节。
    /// </remarks>
    SmsSendFailed = 11003,

    /// <summary>验证码错误（手机号登录换会话，<c>ucenter/users/login</c>）。</summary>
    VerifyCodeWrong = 11004,

    /// <summary>手机号校验失败（<c>ucenter/mobile/check</c>）。</summary>
    MobileCheckFailed = 11053,

    /// <summary>歌曲已下线：提示用户，**不要重试**。</summary>
    TrackOffline = 20012,

    /// <summary>不可播放（实测文案「没有解锁付费歌曲」）。</summary>
    NotPlayable = 20018,

    /// <summary>该曲目不支持分享（分享上报返回，文档 2.8）。**不是失败**，链接照旧能复制。</summary>
    ShareUnsupported = 23006,

    /// <summary>
    /// 这首歌没有 MV（<c>service/mv/info</c> 返回，文案「获取MV失败」）。**不是失败**，
    /// 界面该把 MV 入口收起来。见 <c>reverse/findings/15-mv.md</c>。
    /// </summary>
    MvUnavailable = 20048,

    /// <summary>响应体不是合法的 JSON 信封。</summary>
    MalformedResponse = -2,
}

public static class BodianErrorCodeExtensions
{
    public static BodianErrorCode FromRaw(int code) => code switch
    {
        200 => BodianErrorCode.Success,
        402 => BodianErrorCode.MissingHeaders,
        439 => BodianErrorCode.SignInvalid,
        11012 => BodianErrorCode.NeedAuth,
        11027 => BodianErrorCode.LoginPending,
        11003 => BodianErrorCode.SmsSendFailed,
        11004 => BodianErrorCode.VerifyCodeWrong,
        11053 => BodianErrorCode.MobileCheckFailed,
        20012 => BodianErrorCode.TrackOffline,
        20018 => BodianErrorCode.NotPlayable,
        23006 => BodianErrorCode.ShareUnsupported,
        20048 => BodianErrorCode.MvUnavailable,
        _ => BodianErrorCode.Unknown,
    };
}
