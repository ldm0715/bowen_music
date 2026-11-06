using System.Text.Json;
using Bowen.Core.Models;
using Bowen.Core.Services.Implementations;

namespace Bowen.Core.Services;

/// <summary>
/// 启动时加载统一配置，必要时从旧设置迁移。
/// </summary>
/// <remarks>
/// <para>
/// <b>在 XAML 初始化之前同步调用。</b> 输入法兼容、界面字体这些设置必须在 UI 起来之前就绪，
/// 统一后正好一次读完，不必再各开一次文件。
/// </para>
/// <para>
/// <b>迁移只发生一次。</b> 写入 <c>schemaVersion</c> 之后不再导入旧文件；
/// 旧文件归档失败时，下次启动只补做归档，不回读旧值。
/// </para>
/// <para>
/// 真正的解析与 section 级容错在 <see cref="JsonAppSettingsStore"/>；
/// 这里只负责判定「用主文件 / 备份 / 迁移 / 默认值」和收尾归档。
/// </para>
/// </remarks>
public static class SettingsBootstrap
{
    /// <summary>加载配置。</summary>
    /// <param name="rootDirectory">
    /// 数据目录；默认 <see cref="AppPaths.LocalAppData"/>。测试注入临时目录，不碰真实数据。
    /// </param>
    /// <param name="warning">诊断信息出口。启动早期还没有日志器时由调用方缓冲。</param>
    public static SettingsBootstrapResult Load(
        string? rootDirectory = null,
        Action<string>? warning = null)
    {
        var root = rootDirectory ?? AppPaths.LocalAppData;
        var paths = new SettingsPaths(root);
        var warnings = new List<string>();

        void Warn(string message)
        {
            warnings.Add(message);
            warning?.Invoke(message);
        }

        if (File.Exists(paths.ConfigFile))
        {
            var primary = TryReadUnified(paths.ConfigFile);

            if (primary.Readable && IsSupported(primary))
            {
                ArchiveLeftovers(root, Warn);
                return new SettingsBootstrapResult(primary.Settings, false, warnings);
            }

            if (primary.Readable)
            {
                // 有 JSON 但结构不认识（没有版本，或来自更新版本）。先保留，再迁移。
                if (!Preserve(paths.ConfigFile, ".unrecognized-", Warn))
                {
                    Warn("配置文件结构无法识别且无法保留；本次按内存值继续，不覆盖原文件。");
                    return new SettingsBootstrapResult(primary.Settings, false, warnings);
                }
            }
            else
            {
                var backup = TryReadUnified(paths.BackupFile);
                if (backup.Readable && IsSupported(backup))
                {
                    if (!Preserve(paths.ConfigFile, ".corrupt-", Warn))
                    {
                        Warn("配置主文件损坏且无法保留；本次使用备份内容，不覆盖原文件。");
                        return new SettingsBootstrapResult(backup.Settings, false, warnings);
                    }

                    backup.Settings.SchemaVersion = AppSettings.CurrentSchemaVersion;

                    if (Restore(paths.ConfigFile, backup.Settings))
                    {
                        ArchiveLeftovers(root, Warn);
                    }
                    else
                    {
                        Warn("备份配置读取成功，但写回主文件失败；本次仍使用备份内容。");
                    }

                    return new SettingsBootstrapResult(backup.Settings, false, warnings);
                }

                // 主文件与备份都坏：保留主文件，用默认值。旧文件可能已经归档，不再回读。
                if (!Preserve(paths.ConfigFile, ".corrupt-", Warn))
                {
                    Warn("配置文件和备份都无法读取，且主文件无法保留；本次使用默认设置。");
                    return new SettingsBootstrapResult(AppSettings.Default, false, warnings);
                }

                Warn("配置文件和备份都无法读取，本次使用默认设置。");
                return new SettingsBootstrapResult(AppSettings.Default, false, warnings);
            }
        }

        // 没有可用的统一文件：从 18 份旧设置迁移。
        var migrated = LegacySettingsReader.Read(root);
        migrated.SchemaVersion = AppSettings.CurrentSchemaVersion;

        if (Restore(paths.ConfigFile, migrated))
        {
            LegacySettingsArchive.Archive(root, Warn);
        }
        else
        {
            Warn("统一配置文件写入失败，本次使用迁移结果，下次启动会重试。");
        }

        return new SettingsBootstrapResult(migrated, true, warnings);
    }

    private readonly record struct UnifiedRead(
        bool Readable,
        AppSettings Settings,
        bool HasVersion,
        int Version);

    private static UnifiedRead TryReadUnified(string path)
    {
        if (!File.Exists(path))
        {
            return new UnifiedRead(false, AppSettings.Default, false, 0);
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(bytes);

            var version = 0;
            var hasVersion = JsonAppSettingsStore.TryGetProperty(
                    document.RootElement, "schemaVersion", out var element)
                && element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out version);

            var settings = JsonAppSettingsStore.Deserialize(bytes, out _);
            return new UnifiedRead(true, settings, hasVersion, version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UnifiedRead(false, AppSettings.Default, false, 0);
        }
    }

    /// <summary>只接受有版本、且不高于当前版本的配置。低于当前版本的升级留到以后加迁移函数。</summary>
    private static bool IsSupported(UnifiedRead read) =>
        read.HasVersion && read.Version >= 1 && read.Version <= AppSettings.CurrentSchemaVersion;

    private static bool Restore(string path, AppSettings settings)
    {
        using var store = new JsonAppSettingsStore(settings, path);
        return store.UpdateAndFlush(_ => { });
    }

    /// <summary>配置已经有效，但上一次可能卡在写盘与归档之间；这里补做归档。</summary>
    private static void ArchiveLeftovers(string rootDirectory, Action<string> warn)
    {
        if (LegacySettingsCatalog.All.Any(
                name => File.Exists(Path.Combine(rootDirectory, name))))
        {
            LegacySettingsArchive.Archive(rootDirectory, warn);
        }
    }

    /// <summary>把无法使用的配置改名保留，不直接覆盖。</summary>
    /// <returns>改名成功返回 <c>true</c>。失败时调用方不应覆盖原文件。</returns>
    private static bool Preserve(string path, string marker, Action<string> warn)
    {
        try
        {
            var target = UniquePath(path + marker + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            File.Move(path, target);
            warn($"配置文件无法使用，已保留为 {Path.GetFileName(target)}。");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warn($"配置文件无法使用，且保留失败：{ex.Message}");
            return false;
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var suffix = 1;
        while (File.Exists($"{path}-{suffix}"))
        {
            suffix++;
        }

        return $"{path}-{suffix}";
    }
}

/// <summary>启动加载结果。</summary>
/// <param name="Settings">本次使用的配置。</param>
/// <param name="Migrated">是否执行了旧设置迁移。</param>
/// <param name="Warnings">启动期诊断信息，供调用方在日志器就绪后补记。</param>
public sealed record SettingsBootstrapResult(
    AppSettings Settings,
    bool Migrated,
    IReadOnlyList<string> Warnings);
