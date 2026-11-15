using System.Text.Json.Serialization;

namespace Bowen.Core.Api.Dto.Requests;

/// <summary>
/// <c>service/music/record</c> 的请求体。
/// </summary>
/// <remarks>
/// <para>
/// 形状来自 5.9.8 arm64 静态反汇编（<c>ApiService::behavior</c> 与它的 map 闭包），
/// 2026-11-14 已实网确认：写入后播放次数、时长、周排行都会更新。
/// </para>
/// <para>
/// <b><see cref="Operate"/> 是整数</b>，不是字符串：<c>0</c> 播放完成、<c>1</c> 切歌、
/// <c>5</c> 播放中。其余值属于分享 / 收藏 / 搜索等行为，本项目不用。
/// </para>
/// </remarks>
internal sealed record PlayRecordBody
{
    [JsonPropertyName("musicList")] public required PlayRecordItem[] MusicList { get; init; }

    [JsonPropertyName("operate")] public required int Operate { get; init; }
}

/// <summary>
/// 一条播放记录。官方只发这五个字段，不是完整歌曲对象。
/// </summary>
internal sealed record PlayRecordItem
{
    [JsonPropertyName("id")] public required long Id { get; init; }

    /// <summary>官方取 <c>Song.isUserDefault</c>。本项目没有对应概念，固定 <c>false</c>。</summary>
    [JsonPropertyName("src")] public required bool Src { get; init; }

    /// <summary>曲目总时长，<b>秒</b>。</summary>
    [JsonPropertyName("currentDur")] public required int CurrentDur { get; init; }

    /// <summary>已播位置，<b>秒</b>，夹在 <c>0..CurrentDur</c>。</summary>
    [JsonPropertyName("currentSpt")] public required int CurrentSpt { get; init; }

    /// <summary>播放来源。官方是歌曲对象里的一串归属信息，本项目没有，固定空串。</summary>
    [JsonPropertyName("pSrc")] public required string PSrc { get; init; }
}
