namespace Bowen.Core.Models;

/// <summary>
/// 更新偏好（明文 JSON）。
/// </summary>
public sealed record UpdatePreferences
{
    /// <summary>
    /// 启动时自动检查。<b>默认开</b>：默认关掉的「检查更新」等于没有，
    /// 而这个动作只是一次 GET，不涉及账号、也不上报任何东西。
    /// </summary>
    public bool AutoCheck { get; set; } = true;

    /// <summary>
    /// 用户点过「跳过此版本」的那个版本号（如 <c>0.2.0</c>）。空串 = 没跳过任何版本。
    /// </summary>
    public string SkippedVersion { get; set; } = "";

    public static UpdatePreferences Default { get; } = new();

    /// <summary>保留旧的构造签名，供现有调用点使用。</summary>
    public UpdatePreferences()
    {
    }

    /// <summary>保留旧的构造签名，供现有调用点使用。</summary>
    public UpdatePreferences(bool AutoCheck = true, string SkippedVersion = "")
    {
        this.AutoCheck = AutoCheck;
        this.SkippedVersion = SkippedVersion;
    }

    /// <summary>
    /// 这个版本是不是用户已经表示过不想被提示了。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>只对自动检查生效</b>：用户主动点「检查更新」时不给结果才是奇怪的，
    /// 所以手动那条路不看这个判据。
    /// </para>
    /// <para>
    /// <b>比的是「小于等于」不是「等于」</b>：跳过的版本号解析不出来（用户手改过文件、
    /// 或格式变了）时当作没跳过，宁可多提示一次，也不要因为一个坏字符串就永远静默。
    /// </para>
    /// </remarks>
    public bool IsSkipped(Version latest)
    {
        ArgumentNullException.ThrowIfNull(latest);

        return Version.TryParse(SkippedVersion.Trim(), out var skipped) && latest <= skipped;
    }
}
