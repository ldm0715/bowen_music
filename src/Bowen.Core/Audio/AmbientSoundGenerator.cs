using Bowen.Core.Services;

namespace Bowen.Core.Audio;

/// <summary>
/// 环境音素材的定位与解压。
/// </summary>
/// <remarks>
/// <para>
/// 12 个环境音循环以 <c>EmbeddedResource</c> 编译进 <c>Bowen.Core.dll</c>，安装目录里没有散文件。
/// 首次选中时解压到 <c>%LOCALAPPDATA%\Bowen\ambient\v9\</c>，之后直接复用；
/// 用户误删缓存时下次选中会自动重建。
/// </para>
/// <para>
/// 格式：OGG Vorbis（多数）或 FLAC（心跳），保留源采样率与声道。素材来源见
/// <c>docs/audio-effects.md</c>。
/// </para>
/// </remarks>
public static class AmbientSoundGenerator
{
    /// <summary>缓存版本。替换素材时递增，旧缓存会自然失效。</summary>
    private const string CacheVersion = "v9";

    /// <summary>支持的资源扩展名，按优先级排列。</summary>
    private static readonly string[] Extensions = [".ogg", ".flac"];

    /// <summary>
    /// 返回可直接交给 <c>amovie</c> 的环境音文件路径；必要时从程序集解压。
    /// </summary>
    /// <param name="sound">目录里的环境音定义。</param>
    /// <param name="directory">缓存目录；测试可显式指定。</param>
    public static string Ensure(
        AmbientSoundDefinition sound,
        string? directory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sound);
        cancellationToken.ThrowIfCancellationRequested();

        var assembly = typeof(AmbientSoundGenerator).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        string? resourceName = null;
        foreach (var suffix in Extensions)
        {
            resourceName = resourceNames.FirstOrDefault(
                name => name.EndsWith("." + sound.Id + suffix, StringComparison.OrdinalIgnoreCase));
            if (resourceName is not null)
            {
                break;
            }
        }

        if (resourceName is null)
        {
            throw new FileNotFoundException($"程序集里没有环境音资源：{sound.Id}", sound.Id);
        }

        var extension = resourceName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
            ? ".ogg"
            : ".flac";

        var folder = directory ?? Path.Combine(AppPaths.AmbientSoundsDirectory, CacheVersion);
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, sound.Id + extension);
        if (File.Exists(path) && new FileInfo(path).Length > 1024)
        {
            return path;
        }

        using var source = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"无法打开环境音资源：{resourceName}", resourceName);

        var temporary = path + ".tmp";
        using (var destination = File.Create(temporary))
        {
            source.CopyTo(destination);
        }

        File.Move(temporary, path, overwrite: true);
        return path;
    }
}
