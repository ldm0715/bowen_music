using System.Text;

namespace Bowen.Core.Services;

/// <summary>
/// 迁移成功后把旧设置文件移进归档目录，并写一份说明。
/// </summary>
/// <remarks>
/// <para>
/// 归档只搬 <see cref="LegacySettingsCatalog.All"/> 这 18 份。凭据、账号级数据、
/// 设备标识、缓存与日志一概不动 —— 它们不是配置，也不能跟着设置一起搬。
/// </para>
/// <para>
/// <b>应用不自动删除归档。</b> 删除由用户决定；归档目录本身是回退旧版本的唯一凭据。
/// </para>
/// </remarks>
internal static class LegacySettingsArchive
{
    public const string ReadmeFileName = "README.txt";

    public const string ReadmeText =
        "这些是旧版设置文件，已迁移到应用数据目录的 config.json。\n" +
        "新版本使用正常的话，可以删除本目录；\n" +
        "需要回退到旧版本时，先把这些文件放回应用数据目录。\n";

    /// <summary>归档当前还在根目录里的旧设置文件。</summary>
    /// <returns>至少移动了一个文件时返回 <c>true</c>。</returns>
    public static bool Archive(string rootDirectory, Action<string>? warning = null)
    {
        var existing = LegacySettingsCatalog.All
            .Where(name => File.Exists(Path.Combine(rootDirectory, name)))
            .ToList();

        if (existing.Count == 0)
        {
            return false;
        }

        var target = ChooseTargetDirectory(rootDirectory);

        try
        {
            Directory.CreateDirectory(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning?.Invoke($"创建旧设置归档目录失败：{ex.Message}");
            return false;
        }

        var movedAny = false;
        foreach (var name in existing)
        {
            try
            {
                File.Move(Path.Combine(rootDirectory, name), Path.Combine(target, name));
                movedAny = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warning?.Invoke($"旧设置文件归档失败（{name}）：{ex.Message}");
            }
        }

        try
        {
            File.WriteAllText(
                Path.Combine(target, ReadmeFileName),
                ReadmeText,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning?.Invoke($"归档说明写入失败：{ex.Message}");
        }

        return movedAny;
    }

    /// <summary>
    /// 选归档目录。已有归档且里面有旧文件时改用带时间戳的子目录，不覆盖上一次。
    /// </summary>
    private static string ChooseTargetDirectory(string rootDirectory)
    {
        var archiveRoot = Path.Combine(rootDirectory, "archive");
        var baseDirectory = Path.Combine(archiveRoot, "legacy-settings");

        if (!Directory.Exists(baseDirectory))
        {
            return baseDirectory;
        }

        var occupied = LegacySettingsCatalog.All.Any(
            name => File.Exists(Path.Combine(baseDirectory, name)));

        if (!occupied)
        {
            return baseDirectory;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var candidate = Path.Combine(archiveRoot, $"legacy-settings-{timestamp}");
        var suffix = 1;
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(archiveRoot, $"legacy-settings-{timestamp}-{suffix++}");
        }

        return candidate;
    }
}
