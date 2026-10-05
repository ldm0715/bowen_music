using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 歌词页偏好的本地存储。零网络、零真实用户目录。
/// </summary>
/// <remarks>
/// 与 <see cref="DesktopLyricsSettingsStoreTests"/> 同一套形状：读写、原子替换、
/// 坏文件回默认且不删。默认值这条是本文件独有的 —— <b>默认显示译文</b>，
/// 因为它本来就在歌词内容里，这个开关只是给用户一个关掉的办法。
/// </remarks>
public sealed class LyricsSettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bodian-lyrics-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "lyrics.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonLyricsSettingsStore NewStore() => new(Path_);

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, json);
    }

    [Fact]
    public void NoFile_FallsBackToShowingTranslation()
    {
        Assert.True(NewStore().Load().ShowTranslation);
    }

    [Fact]
    public void RoundTripsTheChoice()
    {
        NewStore().Save(new LyricsSettings { ShowTranslation = false });

        Assert.False(NewStore().Load().ShowTranslation);
    }

    /// <summary>写盘的字段名与其它设置一致：属性名原样，不转驼峰。</summary>
    [Fact]
    public void Save_WritesTheFieldNameTheOtherSettingsUse()
    {
        NewStore().Save(new LyricsSettings { ShowTranslation = false });

        Assert.Contains("\"ShowTranslation\"", File.ReadAllText(Path_), StringComparison.Ordinal);
    }

    [Fact]
    public void BrokenFile_FallsBackToDefaultAndKeepsTheFile()
    {
        WriteRaw("{ 这不是 JSON");

        Assert.True(NewStore().Load().ShowTranslation);

        // 坏文件留着，方便人工看一眼出了什么事。
        Assert.True(File.Exists(Path_));
    }

    /// <summary>
    /// 缺字段时落到 <b>CLR 默认值</b>（<c>false</c>），不是属性上写的那个初始值。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 源生成器给 record 生成的是「有参构造」那条快路径
    /// （<c>ObjectWithParameterizedConstructorCreator</c>，见
    /// <c>obj/.../LyricsSettingsJsonContext.LyricsSettings.g.cs</c>），
    /// 缺的字段会被塞 <c>default</c>，属性初始值根本不参与。
    /// </para>
    /// <para>
    /// <b>这对本项目每一份设置 record 都成立</b>，不是这份独有的 ——
    /// <c>DesktopLyricsSettings.PassThrough</c> 声明成默认开，同样会被 <c>{}</c> 关掉。
    /// 实际踩不到：<c>Save</c> 每次写完整的一份，缺字段只可能来自手工改文件。
    /// </para>
    /// <para>
    /// 这条测试是把行为钉住，省得以后有人看见 <c>{}</c> 读成 false 就去「修」属性初始值 ——
    /// 修不动。真要改，得动的是这份 record 的形状，那是全项目一起的事。
    /// </para>
    /// </remarks>
    [Fact]
    public void MissingField_FallsBackToTheClrDefault_NotTheInitializer()
    {
        WriteRaw("{}");

        Assert.False(NewStore().Load().ShowTranslation);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFile()
    {
        NewStore().Save(new LyricsSettings { ShowTranslation = false });

        Assert.False(File.Exists(Path_ + ".tmp"));
    }

    [Fact]
    public void Save_CreatesTheDirectory()
    {
        Assert.False(Directory.Exists(_directory));

        NewStore().Save(new LyricsSettings());

        Assert.True(File.Exists(Path_));
    }
}
