namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 小窗（迷你播放器）的位置记忆。
/// </summary>
/// <remarks>
/// <b>与 <see cref="IWindowPlacementStore"/> 是同一种东西，只是换一个文件。</b>
/// 拆成两个接口只有一个目的：容器里不能同时注册两个 <see cref="IWindowPlacementStore"/>，
/// 而主窗口、桌面歌词条与小窗必须各记各的 —— 共用文件会让几份记录互相覆盖，
/// 且只会在「先关一个窗、再关另一个」时暴露。
/// </remarks>
public interface IMiniPlayerPlacementStore : IWindowPlacementStore;
