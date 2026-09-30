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

    /// <summary>扫码已扫未确认（仅登录轮询）。**继续轮询，不算失败。**</summary>
    LoginPending = 11027,

    /// <summary>歌曲已下线：提示用户，**不要重试**。</summary>
    TrackOffline = 20012,

    /// <summary>不可播放（实测文案「没有解锁付费歌曲」）。</summary>
    NotPlayable = 20018,

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
        20012 => BodianErrorCode.TrackOffline,
        20018 => BodianErrorCode.NotPlayable,
        _ => BodianErrorCode.Unknown,
    };
}
