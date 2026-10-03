namespace Bodian.Core.Api;

/// <summary>分享上报的结果。</summary>
/// <remarks>
/// <b>上报失败不影响复制链接</b>：链接是本地拼的，只有「分享数 +1」这一步依赖服务端。
/// </remarks>
public enum ShareOutcome
{
    /// <summary>已上报，分享数 +1。</summary>
    Succeeded,

    /// <summary>服务端说该内容不支持分享（业务码 <c>23006</c>）。链接照旧可用。</summary>
    Unsupported,

    /// <summary>请求失败。</summary>
    Failed,
}
