namespace Bodian.Core.Models;

/// <summary>
/// 可以被「清除缓存」清掉的一项。
/// </summary>
/// <remarks>
/// <b>只有这四类。</b> 凭据、窗口位置、各种偏好都不在里面 ——
/// 「清除缓存」不该把用户设过的东西一起清掉。
/// </remarks>
public enum StorageItemKind
{
    /// <summary>封面图片的磁盘缓存。</summary>
    CoverCache,

    /// <summary>「最近播放」记录。</summary>
    PlayHistory,

    /// <summary>搜索关键词历史。</summary>
    SearchHistory,

    /// <summary>日志文件。<b>当天那一份删不掉</b>（Serilog 正持有它）。</summary>
    Logs,
}

/// <summary>某一项的占用。大小一律是磁盘上的实际字节数。</summary>
/// <param name="Item">哪一项。</param>
/// <param name="Bytes">占用字节数。目录不存在时是 0。</param>
/// <param name="FileCount">文件数。单文件的那几项是 0 或 1。</param>
public sealed record StorageUsage(StorageItemKind Item, long Bytes, int FileCount)
{
    /// <summary>给界面显示的文案，如 <c>12.3 MB</c>。</summary>
    public string SizeText => ByteSizeLabel.Format(Bytes);
}

/// <summary>清理一项的结果。</summary>
/// <param name="Item">哪一项。</param>
/// <param name="FilesRemoved">删掉的文件数。</param>
/// <param name="BytesFreed">腾出来的字节数。</param>
/// <param name="Message">给用户看的一句话。全部成功时是空的。</param>
public sealed record StorageClearOutcome(
    StorageItemKind Item,
    int FilesRemoved,
    long BytesFreed,
    string Message = "")
{
    public bool Succeeded => Message.Length == 0;

    /// <summary>结果文案，直接可以挂在设置行上。</summary>
    public string ResultText => Succeeded
        ? $"已清理 {ByteSizeLabel.Format(BytesFreed)}"
        : Message;
}
