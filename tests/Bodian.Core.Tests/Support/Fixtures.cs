using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Bodian.Core.Tests.Support;

/// <summary>
/// 定位并读取仓库根的 <c>fixtures/</c>。
/// </summary>
/// <remarks>
/// <para>
/// 位置由编译期注入的 <c>AssemblyMetadata:RepositoryRoot</c> 提供（见 csproj），
/// 因为测试项目的输出目录（<c>bin/Debug/net10.0/</c>）离仓库根有四层。
/// </para>
/// <para>
/// <b>刻意不用 <c>&lt;Content CopyToOutputDirectory&gt;</c> 把 fixture 拷进输出目录</b>——
/// 那份副本会悄悄变旧，测试会在过期数据上「通过」。这里始终读源文件。
/// </para>
/// <para>
/// 也<b>刻意不引用 <c>tools/Bodian.Probe</c></b>：它是一次性工具，
/// 把它变成主工程的测试依赖等于把 P0 的临时产物固化下来。
/// </para>
/// </remarks>
internal static class Fixtures
{
    public static string Root { get; } = ResolveRoot();

    public static string PathOf(string fileName) => Path.Combine(Root, fileName);

    public static string Read(string fileName) => File.ReadAllText(PathOf(fileName));

    /// <summary>
    /// 读 fixture 并把信封的 <c>data</c> 子树反序列化成 <typeparamref name="T"/>。
    /// </summary>
    /// <remarks>
    /// **必须走 <c>data</c> 子树**：fixture 存的是完整响应（<c>{ code, msg, reqId, data }</c>），
    /// 直接把整份文件当成 DTO 反序列化会得到全是默认值的空对象，而且**不报错**——
    /// 这正是传输层要把信封与 data 分开处理的原因（源生成不支持开放泛型，见
    /// <c>Api/Dto/README.md</c>）。
    /// </remarks>
    public static T Load<T>(string fileName, JsonTypeInfo<T> typeInfo)
    {
        using var doc = JsonDocument.Parse(Read(fileName));
        if (!doc.RootElement.TryGetProperty("data", out var data))
        {
            throw new InvalidOperationException($"{fileName} 没有 data 字段（业务码不是 200？）");
        }

        return JsonSerializer.Deserialize(data, typeInfo)
               ?? throw new InvalidOperationException($"{fileName} 的 data 解析结果为 null");
    }

    private static string ResolveRoot()
    {
        var fromMetadata = typeof(Fixtures).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryRoot")
            ?.Value;

        if (!string.IsNullOrWhiteSpace(fromMetadata))
        {
            var dir = Path.GetFullPath(Path.Combine(fromMetadata, "fixtures"));
            if (Directory.Exists(dir))
            {
                return dir;
            }
        }

        // 兜底：从输出目录向上找带 docs/ 的目录
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "docs")))
            {
                return Path.Combine(current.FullName, "fixtures");
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "找不到 fixtures/。检查 tests/Bodian.Core.Tests.csproj 里的 AssemblyMetadata:RepositoryRoot。");
    }
}
