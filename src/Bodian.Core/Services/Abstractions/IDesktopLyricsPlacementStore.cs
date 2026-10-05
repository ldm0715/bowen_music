namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 桌面歌词条的位置与宽度记忆。
/// </summary>
/// <remarks>
/// <para>
/// <b>与 <see cref="IWindowPlacementStore"/> 是同一种东西，只是换一个文件。</b>
/// 拆成两个接口只有一个目的：容器里不能同时注册两个 <see cref="IWindowPlacementStore"/>，
/// 而主窗口与歌词条必须各记各的 —— 共用文件会让两份记录互相覆盖，
/// 且只会在「先关歌词条、再关主窗口」时暴露。
/// </para>
/// <para>
/// 高度每次写盘时真实记录，读回来时忽略：高度由内容与字号决定，由窗口自己算。
/// </para>
/// </remarks>
public interface IDesktopLyricsPlacementStore : IWindowPlacementStore;
