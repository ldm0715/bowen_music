using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 骨架冒烟测试：确认 xunit.v3 + OutputType=Exe + global.json 的 MTP 选择这条链是通的。
/// 真实测试从 DTO、签名、分页、脱敏几组开始加。
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public void TestHost_IsWired() => Assert.True(true);
}
