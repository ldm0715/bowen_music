namespace Bodian.Probe;

/// <summary>
/// 探针用的数据目录。
/// </summary>
/// <remarks>
/// <para>
/// <b>目录名必须与 <c>Bodian.Core.Services.AppPaths</c> 里的逐字一致。</b>
/// 探针刻意不引用 Core（签名器、设备标识都是独立副本，见 <c>BodianSigner.cs</c> 的注释），
/// 所以路径也在这儿再写一份 —— 两边不一致会让 <c>devid.txt</c> 与 <c>session.dat</c>
/// 各写各的，「探针与客户端共用设备标识」那个前提就没了。
/// </para>
/// <para>
/// 单独一个类而不是在两个文件里各写一遍常量：改目录名时只改这一处。
/// </para>
/// </remarks>
internal static class ProbePaths
{
    public static string LocalAppData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Bowen");
}
