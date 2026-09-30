namespace Bodian.Probe;

/// <summary>
/// 真实响应样本落盘。P1 的单元测试全靠这些样本，所以脱敏在这里统一做掉。
/// 目录固定在仓库根的 fixtures/，与探针工具本身的生死解耦。
/// </summary>
internal static class FixtureStore
{
    public static string Root { get; } = ResolveRoot();

    public static string Save(string name, string rawBody)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, name + ".json");
        File.WriteAllText(path, Sanitizer.ForFixture(rawBody));
        return path;
    }

    public static string SaveText(string name, string extension, string content)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, name + extension);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>从 exe 所在目录向上找带 docs/ 的仓库根；找不到就退回当前工作目录。</summary>
    private static string ResolveRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "docs")))
            {
                return Path.Combine(dir.FullName, "fixtures");
            }

            dir = dir.Parent;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "fixtures");
    }
}
