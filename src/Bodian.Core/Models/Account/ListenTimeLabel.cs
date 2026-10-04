using System.Globalization;

namespace Bodian.Core.Models.Account;

/// <summary>
/// 听歌时长文案。
/// </summary>
/// <remarks>
/// <para>
/// <b>单位假设集中在 <see cref="SecondsPerUnit"/> 一个常量上。</b>服务端只给一个整数，
/// 没说单位；实测（2026-10-04）<c>playTime=186344 / playcnt=1110 ≈ 168</c> 秒每首，
/// 与单曲平均时长吻合，所以是秒。若日后发现别的端点单位不同，改这一个数就够。
/// </para>
/// <para>
/// <b>换端点时的定单位办法</b>：<c>playTime / playcnt</c> 应落在几十到几百之间；
/// 若是几万则单位是毫秒，若是 1 上下则是分钟。
/// </para>
/// </remarks>
public static class ListenTimeLabel
{
    /// <summary>一个 <c>playTime</c> 单位等于多少秒。实测确认为 1（即原值就是秒）。</summary>
    private const long SecondsPerUnit = 1;

    /// <summary>不知道（<c>null</c> / 非正数）时返回空串，由调用方决定显示「—」还是整行收掉。</summary>
    public static string Format(long? raw)
    {
        if (raw is not { } value || value <= 0)
        {
            return "";
        }

        var seconds = value * SecondsPerUnit;
        var hours = seconds / 3600;

        return hours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{hours} 小时")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, seconds / 60)} 分钟");
    }
}
