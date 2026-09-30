using Bodian.Core.Api.Dto;

namespace Bodian.Core.Api;

/// <summary>
/// 从曲目的 <c>payInfo.feeType</c> 判读付费属性。
/// </summary>
/// <remarks>
/// <para>
/// <b>只用于界面展示。</b> 能不能播一律由服务端的 <c>checkRight</c> 裁决，
/// 客户端不拿这个字段做任何权限推断。
/// </para>
/// <para>
/// <c>feeType</c> 是「费用类型 → <c>"1"</c>/<c>"0"</c>」的字典，<b>键集合会随接口变</b>：
/// <c>music/info</c> 有 <c>vip</c>/<c>song</c>/<c>album</c>/<c>bookvip</c>/<c>bodianAlbum</c>，
/// <c>search/music/list</c> 只有 <c>vip</c>/<c>song</c>/<c>bodianAlbum</c>。
/// 所以按键查而不是读固定属性。
/// </para>
/// <para>
/// <b>⚠️ 判据尚未闭环验证。</b> 现有样本（5 条）全部是付费曲，
/// <c>feeType</c> 都是 <c>{vip:"1", song:"1"}</c>，
/// <b>没有任何一首免费曲作对照</b>——也就是说「<c>vip="0"</c> 就代表免费」这一步仍是推断。
/// 采到免费曲样本后必须回来核对这个函数，以及界面上那个徽标会不会误报。
/// </para>
/// </remarks>
internal static class PayTypeReader
{
    /// <summary>需要 VIP，以及需要单曲/专辑购买。</summary>
    public static (bool RequiresVip, bool RequiresPurchase) Resolve(PayInfoDto? payInfo)
    {
        var feeType = payInfo?.FeeType;

        if (feeType is null || feeType.Count == 0)
        {
            // 没有 feeType 就说不上付费 —— 但也别当成「免费」，两个标志都留 false 即可。
            return (false, false);
        }

        var requiresVip = IsSet(feeType, "vip");

        var requiresPurchase = IsSet(feeType, "song")
            || IsSet(feeType, "album")
            || IsSet(feeType, "bodianAlbum")
            || IsSet(feeType, "bookvip");

        return (requiresVip, requiresPurchase);
    }

    private static bool IsSet(Dictionary<string, string> feeType, string key) =>
        feeType.TryGetValue(key, out var value) && value == "1";
}
