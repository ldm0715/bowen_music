using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bodian.Core.Services.Implementations;

/// <inheritdoc cref="IMiniPlayerPlacementStore" />
/// <remarks>
/// <para>
/// <b>只是一层转发，读写逻辑一行都没重写。</b> 落盘那套（先写临时文件再原子替换、
/// 坏文件不删且按无记录处理、读失败返回 <c>null</c>）在 <see cref="JsonWindowPlacementStore"/>
/// 里已经有了，这两件事的差别只有文件路径。
/// </para>
/// <para>
/// <b>为什么需要这么一层。</b> 空的派生接口并不能让 <see cref="JsonWindowPlacementStore"/>
/// 直接满足 <see cref="IMiniPlayerPlacementStore"/> —— 那个类声明的接口里没有它。
/// 而给那个类加上这个接口又会让「主窗口的存储」也自称是小窗的存储，名不副实。
/// </para>
/// </remarks>
public sealed class MiniPlayerPlacementStore : IMiniPlayerPlacementStore
{
    private readonly IWindowPlacementStore _inner;

    /// <param name="path">默认 <see cref="AppPaths.MiniPlayerWindowFile"/>。测试可注入临时路径。</param>
    public MiniPlayerPlacementStore(
        string? path = null,
        ILogger<JsonWindowPlacementStore>? logger = null)
        : this(new JsonWindowPlacementStore(path ?? AppPaths.MiniPlayerWindowFile, logger))
    {
    }

    internal MiniPlayerPlacementStore(IWindowPlacementStore inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
    }

    public WindowPlacement? Load() => _inner.Load();

    public void Save(WindowPlacement placement) => _inner.Save(placement);
}
