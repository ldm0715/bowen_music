using System.Text.Json;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <inheritdoc cref="IWindowPlacementStore" />
/// <remarks>
/// <para>
/// <b>单独一个文件（<c>window.json</c>），不并进 <c>settings.json</c>。</b>
/// 窗口几何是「这台机器的这次会话」的事，与「用户选了什么外观」不是一类 ——
/// 混在一起的话，以后想同步外观设置就得先把它摘出去。
/// </para>
/// <para>
/// <b>读失败一律返回 <c>null</c>，不抛。</b> 坏掉的记录只该让窗口回到默认位置，
/// 不该让应用起不来。<b>且不删坏文件</b> —— 留着还能人工看一眼。
/// </para>
/// <para>
/// <b>这里只负责「存得对不对」，不负责「这块屏幕上放不放得下」。</b>
/// 判断坐标是否还在某块显示器上要问系统，那是 UI 层的事（见 <c>MainWindow</c>）。
/// 这一层只挡掉明显不合法的记录（宽高非正）。
/// </para>
/// </remarks>
public sealed class JsonWindowPlacementStore : IWindowPlacementStore
{
    private readonly string _path;
    private readonly ILogger<JsonWindowPlacementStore> _logger;

    /// <param name="path">默认 <see cref="AppPaths.WindowFile"/>。测试可注入临时路径。</param>
    public JsonWindowPlacementStore(
        string? path = null,
        ILogger<JsonWindowPlacementStore>? logger = null)
    {
        _path = path ?? AppPaths.WindowFile;
        _logger = logger ?? NullLogger<JsonWindowPlacementStore>.Instance;
    }

    public WindowPlacement? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(_path);
            var loaded = JsonSerializer.Deserialize(bytes, WindowPlacementJsonContext.Default.WindowPlacement);

            if (loaded is null)
            {
                return null;
            }

            // 宽高非正的记录没法用来设窗口，当作没有。位置（X/Y）为负是合法的 ——
            // 副屏在主屏左边时就是这样。
            if (loaded.Width <= 0 || loaded.Height <= 0)
            {
                _logger.LogWarning(
                    "窗口记录里的宽高不合法（{Width}x{Height}），按无记录处理",
                    loaded.Width,
                    loaded.Height);

                return null;
            }

            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "窗口记录读取失败，按无记录处理：{Path}", _path);

            return null;
        }
    }

    public void Save(WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        try
        {
            var directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                placement,
                WindowPlacementJsonContext.Default.WindowPlacement);

            // 先写临时文件再原子替换，与其余几个存储同一条规矩。
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不进去只影响「下次启动记不记得住位置」，不该影响退出流程。
            _logger.LogWarning(ex, "窗口记录写入失败：{Path}", _path);
        }
    }
}
