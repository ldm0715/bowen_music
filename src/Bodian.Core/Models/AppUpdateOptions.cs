namespace Bodian.Core.Models;

/// <summary>
/// 更新源的配置。**这是唯一需要改的地方。**
/// </summary>
/// <remarks>
/// <para>
/// 仓库建好之后，把 <see cref="Owner"/> 与 <see cref="Repository"/> 填上，检查更新就通了 ——
/// 不需要改服务、也不需要改界面。
/// </para>
/// <para>
/// <b>绝对不要拿 <c>service/version/check/pc</c> 当更新源。</b> 那个端点返回的是
/// <b>官方客户端</b>的协议版本，跟本项目的版本号毫无关系；而且把请求头里的 <c>ver</c>
/// 跟上去会触发服务端强制签名校验，全站请求会一起挂掉（见 <c>docs/bodian-api-reference.md</c>）。
/// </para>
/// </remarks>
public sealed record AppUpdateOptions
{
    /// <summary>仓库所属账号。空 = 未配置。</summary>
    public string Owner { get; init; } = "";

    /// <summary>仓库名。空 = 未配置。</summary>
    public string Repository { get; init; } = "";

    /// <summary>「项目主页」按钮的目标。空 = 未配置（按钮禁用）。</summary>
    public string ProjectUrl { get; init; } = "";

    /// <summary>当前设置。指向公开仓库，检查更新与「关于」页的两个链接都靠它。</summary>
    public static AppUpdateOptions Default { get; } = new()
    {
        Owner = "ldm0715",
        Repository = "bowen_music",
        ProjectUrl = "https://github.com/ldm0715/bowen_music",
    };

    public bool IsConfigured => Owner.Length > 0 && Repository.Length > 0;

    /// <summary>查最新 release 的接口地址。<b>未配置时是 <c>null</c></b>，调用方据此短路。</summary>
    public Uri? ReleaseApiUri => IsConfigured
        ? new Uri($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest")
        : null;

    /// <summary>项目主页。<b>未配置时是 <c>null</c></b>。</summary>
    public Uri? ProjectUri => ProjectUrl.Length > 0 ? new Uri(ProjectUrl) : null;

    /// <summary>仓库地址。<b>未配置时是 <c>null</c></b>。</summary>
    public Uri? RepositoryUri => IsConfigured ? new Uri($"https://github.com/{Owner}/{Repository}") : null;
}
