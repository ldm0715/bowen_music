namespace Bowen.Core.Services;

/// <summary>
/// 统一配置与迁移相关路径。根目录可注入，测试不碰真实用户目录。
/// </summary>
internal sealed class SettingsPaths(string rootDirectory)
{
    /// <summary>数据目录。生产环境是 <see cref="AppPaths.LocalAppData"/>。</summary>
    public string RootDirectory { get; } = rootDirectory;

    /// <summary>统一配置文件。</summary>
    public string ConfigFile => Path.Combine(RootDirectory, "config.json");

    /// <summary>上一份成功写入的配置。</summary>
    public string BackupFile => ConfigFile + ".bak";

    /// <summary>某一份旧设置文件在数据目录下的完整路径。</summary>
    public string LegacyFile(string fileName) => Path.Combine(RootDirectory, fileName);
}
