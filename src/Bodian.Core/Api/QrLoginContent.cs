namespace Bodian.Core.Api;

/// <summary>
/// 扫码登录时二维码里要承载的内容。
/// </summary>
/// <remarks>
/// <b>必须是完整落地页表单，不能只放 key。</b> 实测：放 <c>qrCode</c> 本身、或
/// <c>...login_pc?qrCode=&lt;key&gt;</c> 都扫不出正确结果，只有下面这个完整 URL 形态可用。
/// 这个字符串常量是实测产物，改动前先重测。
/// </remarks>
internal static class QrLoginContent
{
    private const string LandingPagePrefix =
        "https://bodian-oia.kuwo.cn/bodian/download.html?pageName=login_pc&pt=3&id=";

    /// <summary>
    /// 用二维码 key 拼出扫码内容。
    /// </summary>
    /// <param name="key"><c>ucenter/login/qrCode</c> 返回的 <c>data.qrCode</c>。</param>
    public static Uri ForKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return new Uri(LandingPagePrefix + key, UriKind.Absolute);
    }
}
