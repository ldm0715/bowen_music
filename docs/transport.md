# 波点客户端 · 工程骨架与传输层

**这是 P1 的落地设计稿，代码按本文实现。** 写作日期 2026-09-30。

分工：`tech-stack.md` 回答「**为什么选这些**」（版本、取舍、许可），本文回答「**具体怎么写**」（csproj 全文、类的形状、DTO 字段映射、测试清单、验收命令）。两者冲突时以本文为准，因为这里的属性集是实际构建验证过的。

协议本身的事实（请求头、签名、错误码、时间单位）在 `bodian-api-reference.md`，本文只引用不重复。

**本文所有带 ✅ 的结论都经过实际命令核实，不是推断。** 未核实的项明确标注。

---

## 1. 解决方案骨架

### 1.1 目录

```
bodain_winui/
├── global.json                        SDK 钉版 + 测试平台选择
├── Bodian.sln
├── src/
│   ├── Directory.Build.props          ★ 放这里，不放仓库根（见 1.3）
│   ├── Directory.Packages.props       ★ 同上
│   ├── Bodian.Core/                   net10.0，零 UI 依赖
│   └── Bodian.WinUI/                  net10.0-windows10.0.26100.0
├── tests/
│   ├── Directory.Build.props          Import ..\src\ 那份
│   ├── Directory.Packages.props       Import ..\src\ 那份
│   └── Bodian.Core.Tests/             net10.0，xunit.v3
├── docs/  fixtures/                   （P0 产物，P1 不动）
└── tools/Bodian.Probe/                （P0 探针，P1 不动、不改、不删）
```

`tools/Bodian.Probe` **不进 `Bodian.sln`**。它是一次性工具，进解决方案会让每次构建都被它拖住，也会让"客户端工程"的边界变模糊。

### 1.2 四项已核实的环境事实 ✅（2026-09-30 实测）

这几条与直觉相反，先看这里能省掉返工：

| # | 事实 | 证据 |
| --- | --- | --- |
| 1 | **`dotnet new sln` 在这个 SDK 上默认生成 `.slnx`**（XML 格式），不是 `.sln` | `dotnet new sln --help` 的 `-f, --format <sln\|slnx>` 一行写着 `默认: slnx` |
| 2 | **WinUI 3 没有 CLI 模板。** `dotnet new list winui` 找不到匹配模板；`Microsoft.WindowsAppSDK.Templates` 在 nuget.org 上不存在 | `dotnet new list winui` 退出码 103；模板目录里只有 console/classlib/winforms/wpf/xunit 等 |
| 3 | **`xunit.v3` 要求测试项目 `OutputType=Exe`** | 包的 `buildTransitive` targets 里有 `Condition=" '$(OutputType)' != 'Exe' "` 的 `<Error>` |
| 4 | **`dotnet test` 要走 MTP 必须在 `global.json` 里显式选择** | `dotnet test --help` 首行写明「若要使用 Microsoft.Testing.Platform，请通过 global.json 选择加入」 |

推论：**三个 csproj 全部手写**。`dotnet new classlib` 只会生成一个要逐条替换的空壳；`dotnet new xunit` 生成的是 **xunit v2 (2.9.3)**，与选型 `xunit.v3 4.0.1` 不符且没有 `OutputType=Exe`；WinUI 项目根本没有模板可依。`dotnet new sln` 只用一次，且必须带 `-f sln`。

### 1.3 红线：`Directory.Build.props` / `Directory.Packages.props` 不放仓库根

MSBuild 与 NuGet 对这两个文件是**从项目目录向上逐级查找**的。`tools/Bodian.Probe/` 的祖先链是 `tools/` → 仓库根，所以**根级文件会被探针继承**。

一旦根级 `Directory.Packages.props` 开了 `ManagePackageVersionsCentrally=true`，探针 csproj 里那两行带 `Version=` 的 `PackageReference` 会直接报：

```
NU1008: Projects that use central package version management should not define
        the version on the PackageReference items.
```

**把 P0 探针的构建打挂。** 所以这四个文件放 `src/` 与 `tests/`，仓库根保持干净。验收步骤里有一条专门自检这件事（第 5 节第 2 步）。

### 1.4 `global.json`

```json
{
  "sdk": {
    "version": "10.0.400",
    "rollForward": "latestFeature"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

- `sdk` 段钉住本机已有的 10.0.400，`rollForward: latestFeature` 允许 10.0.5xx。这不会改变 `tools/Bodian.Probe` 的行为（它同样落在 10.0.400 上）
- `test.runner` 是 `dotnet test` 走 MTP 的**唯一开关**。不写它，`dotnet test` 退回 VSTest 模式，而 VSTest 跑 MTP 需要另设 `TestingPlatformDotnetTestSupport=true` 并把参数放到 `--` 之后——那是条更绕的路
- 选了 MTP 之后，`dotnet test` 的调用形态变成 `dotnet test --project <csproj>`（不再接受位置参数形式的项目路径）

### 1.5 `src/Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Deterministic>true</Deterministic>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
  </PropertyGroup>
</Project>
```

`tests/Directory.Build.props` 只写 `<Project><Import Project="..\src\Directory.Build.props" /></Project>`。
`tests/Directory.Packages.props` 同样只写一个 `Import`，指向 `..\src\Directory.Packages.props`。

### 1.6 `src/Directory.Packages.props`

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>false</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <!-- 版本以 tech-stack.md 第 2 节为准，不自行升级 -->
  <ItemGroup>
    <PackageVersion Include="System.Security.Cryptography.ProtectedData" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Logging" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.12" />
    <PackageVersion Include="Microsoft.WindowsAppSDK" Version="2.5.1" />
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageVersion Include="WinUIEx" Version="2.9.3" />
    <PackageVersion Include="Serilog" Version="4.4.0" />
    <PackageVersion Include="Serilog.Extensions.Hosting" Version="10.0.0" />
    <PackageVersion Include="Serilog.Sinks.File" Version="7.0.0" />
    <PackageVersion Include="xunit.v3" Version="4.0.1" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="4.0.0" />
  </ItemGroup>
</Project>
```

`CommunityToolkit.Mvvm` 与 `WinUIEx` **P1 只登记版本、不引用**——避免 P2 时再核一遍版本号。其余包 P1 都用得上。

上表 11 个包的版本号已对 nuget.org 逐个 `HEAD` 确认存在（全部 200）。

### 1.7 `src/Bodian.Core/Bodian.Core.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- ★ 刻意不带 -windows：协议 / 签名 / 歌词解码 / DPAPI 全部可单测 -->
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Bodian.Core</RootNamespace>
    <IsAotCompatible>true</IsAotCompatible>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.Security.Cryptography.ProtectedData" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Bodian.Core.Tests" />
  </ItemGroup>
</Project>
```

两个属性是**机制而非约定**：

- `IsAotCompatible=true` 打开裁剪/AOT 分析器，把任何反射式 JSON 用法变成警告；配合 `TreatWarningsAsErrors` 变成编译错误。这是「DTO 必须走源生成」的强制手段
- `InternalsVisibleTo` 让测试能碰 `internal` 的 DTO 与 transport，从而不必为了测试把它们 `public` 化

### 1.8 `src/Bodian.WinUI/Bodian.WinUI.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
    <UseWinUI>true</UseWinUI>
    <RootNamespace>Bodian.WinUI</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>

    <!-- ★ RuntimeIdentifier 单数且不可省：自包含要把对应架构的原生 DLL 拷进输出目录 -->
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <EnableMsixTooling>true</EnableMsixTooling>

    <!-- 关掉反射序列化：DTO 必须走 JsonSerializerContext -->
    <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
    <PackageReference Include="Serilog" />
    <PackageReference Include="Serilog.Extensions.Hosting" />
    <PackageReference Include="Serilog.Sinks.File" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Bodian.Core\Bodian.Core.csproj" />
  </ItemGroup>
</Project>
```

> **`UseWinUI=true` 在 `tech-stack.md` 第 2 节的片段里漏写了**（那个片段只有 .NET/OutputType/TFM/自包含几项），但 `dev-environment.md` 第 4.1 节的实测路线里有它——XAML 编译靠它，不能省。本节是权威版本。

`RuntimeIdentifier` 为何不可省：开 `WindowsAppSDKSelfContained=true` 后不带 RID 构建会直接失败（`WindowsAppSDKSelfContained requires a supported Windows architecture`）。多架构出包时用复数的 `<RuntimeIdentifiers>` 声明集合，但每次构建仍须指定单个 RID。

### 1.9 `tests/Bodian.Core.Tests/Bodian.Core.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <!-- ★ xunit.v3 的 buildTransitive targets 会对非 Exe 直接 Error -->
    <OutputType>Exe</OutputType>
    <RootNamespace>Bodian.Core.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <!-- xunit 自身大量用反射，不要开 AOT 分析器 -->
    <IsAotCompatible>false</IsAotCompatible>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Bodian.Core\Bodian.Core.csproj" />
  </ItemGroup>

  <!-- ★ 测试定位仓库根：编译期注入，不靠遍历输出目录 -->
  <ItemGroup>
    <AssemblyMetadata Include="RepositoryRoot" Value="$(MSBuildThisFileDirectory)..\..\" />
  </ItemGroup>
</Project>
```

xunit.v3 的配置要点（来自包自带的 `buildTransitive`，多数由包自动设置）：

| 项 | 值 | 谁设的 |
| --- | --- | --- |
| `OutputType` | `Exe` | **必须自己设**，否则校验报错 |
| `UseAppHost` | `true` | 包自动设（apphost 是硬要求） |
| `GenerateProgramFile` | `false` | 包自动设 |
| `GenerateSelfRegisteredExtensions` | `true` | 包自动设 |
| `IsTestingPlatformApplication` | `true` | 包自动设（`dotnet test` 据此判定） |
| `Microsoft.Testing.Platform` | 由 `xunit.v3` 传递引入 | **不要手动引** |
| 入口点 | 自动生成 | **不要自己写 `Main`** |

---

## 2. `Bodian.Core` 的分层与类清单

```
src/Bodian.Core/
├── Api/
│   ├── BodianTransportOptions.cs      终结点 / 请求头 / 超时 / 上限 / 代理
│   ├── BodianHttpVerb.cs              Get | Post
│   ├── BodianRequest.cs               一次请求的形状
│   ├── BodianEnvelope.cs              信封（已解析）
│   ├── BodianErrorCode.cs             200 / 402 / 439 / 11012 / 11027 / 20012 / 20018
│   ├── BodianApiException.cs          非期望业务码时抛；只带 path 与 reqId，不带 query
│   ├── BodianSession.cs               uid / token / Revision / Cleared 事件
│   ├── IBodianTransport.cs            唯一发请求的入口
│   ├── BodianHttpTransport.cs         internal —— 唯一碰 HttpClient 的地方
│   ├── BodianSigner.cs                internal —— 唯一碰 MD5 的地方
│   ├── BodianHeaders.cs               internal —— 请求头表（拉出来单独测）
│   ├── Endpoints.cs                   internal —— 路径常量与 query/body 构造
│   ├── IBodianApi.cs / BodianApi.cs   领域门面
│   ├── Dto/                           见第 3 节
│   ├── Paging/                        见第 4 节
│   └── BodianJsonContext.cs           源生成上下文
├── Diagnostics/
│   ├── SafeUrl.cs                     已脱敏的 URL，ToString 安全
│   ├── LogRedactor.cs                 字符串级脱敏
│   └── RedactingLoggerFactory.cs      ILoggerFactory 装饰器（唯一收口）
├── Lyrics/
│   ├── BodianLyricPayload.cs          query 构造 + 一次 Base64 解码
│   └── KuwoFactorCodec.cs             [kuwo:N] 八进制系数还原
├── Models/
│   ├── Track.cs  TrackArtist.cs  PayInfo.cs
│   └── PlaybackRight.cs               播放权限（含试听区间的语义，不是原始状态码）
└── Services/
    ├── Abstractions/   ICredentialStore.cs  IDeviceIdentity.cs  BodianSessionData.cs
    └── Implementations/ DpapiCredentialStore.cs  FileDeviceIdentity.cs  InMemoryCredentialStore.cs
```

`InMemoryCredentialStore` 是**生产代码**（不是测试专用）——单测和 UI 预览都用它，避免碰真实磁盘。

### 2.1 DPAPI 与纯 `net10.0` 的关系 ✅（已核实，含一处更正）

`docs/backlog.md` 的 P1 约束写着「Core 用纯 `net10.0`、不带 `-windows` TFM」和「凭据只能走 DPAPI」——这两条看着像有冲突。**实测结论：可以共存，但必须给实现类加 `[SupportedOSPlatform("windows")]`。**

核实过的事实：

1. `System.Security.Cryptography.ProtectedData 10.0.12` 的 `lib/` 下有 **`net10.0/`** 资产，nuspec 里有 `<group targetFramework="net10.0" />`（空依赖组）。NuGet 对 `net10.0` 项目精确命中这一组——**能引用、能编译**
2. **⚠️ 更正：CA1416 会触发。** 本文初稿断言「不触发 CA1416」，理由是「运行时源码里的 `ProtectedData` 类没有 `[SupportedOSPlatform]`」。**这条是错的。** 分析器看的是**包在 API 契约层面**标注的平台特性，不是运行时源码——`ProtectedData.Protect` / `Unprotect` / `DataProtectionScope` 都被标成 Windows-only，从平台中立的 `net10.0` 调用直接报错（`IsAotCompatible` + `TreatWarningsAsErrors` 下是**编译失败**，实测 4 个 CA1416）

**所以正确做法是给实现类显式标注**：

```csharp
[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStore : ICredentialStore
```

这比「不触发分析器」更好——**Windows-only 从约定变成了编译期可见**：调用方（WinUI，TFM 是 `net10.0-windows10.0.*`）天然满足，而一个平台中立的项目若误用会直接编译不过。

3. 运行时语义：非 Windows 上调用抛 `PlatformNotSupportedException`。本项目是 Windows-only 桌面应用，不影响设计

那为什么还要 `ICredentialStore` 抽象？**不是为了绕开编译问题**（标注之后本来就能编），而是两个别的目的：

- **可测性**：绝大多数服务层测试不该碰真实磁盘和 DPAPI（慢，且会污染 `%LOCALAPPDATA%\Bodian`），用内存实现替换
- **可替换性**：若日后改成 MSIX 打包，`PasswordVault` 或 `ApplicationData` 可以换进来而不动 Core

因为测试就在 Windows 上跑，`DpapiCredentialStore` 本身**也能被真实单测**（一次 Protect → Unprotect 往返）。测试项目是平台中立的 `net10.0`，所以测试类同样要标 `[SupportedOSPlatform("windows")]` 或用一个 `[WindowsFact]` 保护，让测试套件在没有 DPAPI 的机器上仍能跑完。

> **另一条同类风险（尚未遇到）**：任何其他 Windows-only 的 API 从 Core 调用都会撞上 CA1416。若将来确实需要，**优先把实现挪到 WinUI 层**，而不是给整个 Core 加 `<NoWarn>CA1416</NoWarn>`——后者会让平台上中立的代码悄悄依赖 Windows。

### 2.2 传输层的公开 API

```csharp
namespace Bodian.Core.Api;

public sealed record BodianTransportOptions
{
    public Uri    BaseAddress           { get; init; } = new("https://bd-api.kuwo.cn/api/");
    public string UserAgent             { get; init; } = "Dart/3.3 (dart:io)";
    public string Platform              { get; init; } = "win";
    public string Channel               { get; init; } = "W1";
    public string Version               { get; init; } = "1.1.7";   // ★ 见 2.4 节，不要跟版本
    public string ServerVersion         { get; init; } = "13";
    public string ApiVersion            { get; init; } = "application/json";
    public string Brand                 { get; init; } = "Windows";
    public string Network               { get; init; } = "wifi";
    public TimeSpan RequestTimeout      { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan PooledConnectionLifetime { get; init; } = TimeSpan.FromMinutes(2);
    public long   MaxResponseBytes      { get; init; } = 8L * 1024 * 1024;
    public Uri?   Proxy                 { get; init; }
}

public sealed record BodianRequest
{
    public required string Path { get; init; }              // 不含 /api 前缀
    public BodianHttpVerb Verb  { get; init; } = BodianHttpVerb.Get;
    public IReadOnlyList<KeyValuePair<string, string>> Query { get; init; } = [];
    public string? JsonBody { get; init; }                  // 签名覆盖这份精确字节
    public bool Signed { get; init; }
    /// <summary>除了 200 之外还接受哪些业务码（不抛异常）。</summary>
    public IReadOnlyCollection<BodianErrorCode> AcceptedCodes { get; init; } = [];
}

public sealed record BodianEnvelope<T>(int Code, string? Message, string? RequestId, T? Data);

public interface IBodianTransport
{
    Task<BodianEnvelope<T>> SendAsync<T>(
        BodianRequest request,
        JsonTypeInfo<T> dataTypeInfo,
        CancellationToken cancellationToken = default);

    /// <summary>歌词站在 mlyric.kuwo.cn，不走 /api 前缀、不签名。</summary>
    Task<BodianEnvelope<T>> SendAbsoluteAsync<T>(
        Uri url, JsonTypeInfo<T> dataTypeInfo, CancellationToken cancellationToken = default);
}
```

`JsonTypeInfo<T>` 这个参数是**故意的**：它逼着每个调用点从 `BodianJsonContext` 取类型信息，任何想退化成反射的写法都编译不过。这是把源生成从约定变成约束的第二处机制（第一处是 `JsonSerializerIsReflectionEnabledByDefault=false`）。

```csharp
public sealed class BodianSession
{
    public static BodianSession Anonymous { get; }          // uid = "-1"，token = ""
    public string Uid   { get; }
    public string Token { get; }
    public bool   IsAuthenticated { get; }
    public int    Revision { get; private set; }            // 每次变更自增
    public event EventHandler<BodianSessionClearedEventArgs>? Cleared;

    public void Set(string uid, string token);              // Revision++
    public void Clear();                                    // Revision++，触发 Cleared
    internal void NotifyUnauthorized();                     // 收到 11012 时由 transport 调用
}
```

`Revision` 的用途要写进注释：**请求发出前记下 revision，响应回来后若已变化就丢弃结果**。这是「账号切换后迟到的旧写请求不污染新会话 UI」的落地机制，没有它就只能靠调用点自觉。

拿到 `11012` 时 `transport` 调 `session.NotifyUnauthorized()`（清会话 + 触发事件），**再**抛 `BodianApiException`。清会话语义收敛在 `BodianSession` 里，transport 只负责通知——这样「11012 → 会话被清」和「11012 → 抛出正确错误码」两件事可以分别单测。

### 2.3 传输层实现要点

| 关注点 | 做法 |
| --- | --- |
| **唯一碰 `HttpClient`** | `BodianHttpTransport` 构造注入 `HttpMessageHandler` + `BodianTransportOptions` + `BodianSession` + `TimeProvider` + `ILogger<>`。生产里 handler 是单例 |
| **handler 配置收在一处** | `internal static SocketsHttpHandler CreateHandler(BodianTransportOptions)` —— `AutomaticDecompression` / `PooledConnectionLifetime` / 代理都在这里，并**单独写测试断言这几个属性** |
| **超时** | `HttpClient.Timeout = Timeout.InfiniteTimeSpan`，改由 transport 用 `CancellationTokenSource.CreateLinkedTokenSource(ct)` + `CancelAfter` 包住**整个**「发请求 + 读 body」。因为要用 `ResponseHeadersRead`，`HttpClient.Timeout` 只管到响应头，覆盖不到 body |
| **8 MiB 上限** | 用 `HttpCompletionOption.ResponseHeadersRead`（**不能**用默认的 `ResponseContentRead`，否则缓冲发生在限额之前），然后手写带限额的流拷贝：每次 `ReadAsync` 后累加，超限立即抛。读的是**解压后**的流，所以限额天然作用于解压后字节，**顺带挡住 gzip 炸弹** |
| **gzip** | 显式加 `Accept-Encoding: gzip`，解压由 `SocketsHttpHandler.AutomaticDecompression` 完成 |
| **签名** | query 串由 `BodianSigner.FormUrlEncode` 逐字节产出（**不能换 `Uri.EscapeDataString`**）；`pairs` 顺序固定为 `调用方 query → uid → token → [timestamp → sign]`，与 P0 探针一致，也让黄金用例可复现 |
| **path 形态** | 固定传**不含 `/api` 前缀**的业务路径。探针那三种 `PathForm` 对照开关**不移植**，只保留已定的这一种 |
| **时间戳** | 注入 `TimeProvider`，测试用一个 15 行的 fake 冻结毫秒值，**不引新包** |
| **非 JSON 响应** | 与探针一致：解析失败时保留原文，抛带 `RawBody`（**已过 `LogRedactor`**）的异常，不吞掉网关错误页 |
| **不用 `JsonNode`** | 探针用 `JsonNode` 是因为它要动态展示；Core 改用 `JsonDocument` + `JsonTypeInfo`，这是源生成的前提 |

**必须写进注释的边界**：这个 transport **只服务 API JSON，音频字节流不归它管**。`audioUrl` 拿到的是 CDN 直链，P2/P9 走独立下载路径。否则「总超时 30 秒」会在 P9 把大文件下载掐断。

### 2.4 `ver` 头的长期约束

`Version` 默认 `1.1.7`，**不要随手跟版本**。`bodian-api-reference.md` 1.3 节实测：校验由 `ver` 头控制，`ver ≤ 3.0.0` 服务端完全不校验签名，`ver ≥ 3.5` 强制校验并返回 `439 sign invalid`。

本项目钉死 `1.1.7` 意味着主流程不依赖签名正确；但**签名实现本身仍要留着**——`ver` 一越过门槛，全部请求会一起挂掉，而错误信息只有一句 `sign invalid`，不指向具体哪里错。

---

## 3. DTO 层

### 3.1 两条源生成的硬限制（设计期就会撞上）

1. **不支持开放泛型** → 不能写 `JsonSerializable(typeof(ApiEnvelope<>))`。**所以信封不做 JSON 反序列化**：transport 用 `JsonDocument` 手工取 `code` / `msg` / `reqId`，再把 `data` 子树交给 `JsonSerializer.Deserialize(JsonElement, JsonTypeInfo<T>)`。这在性能上也更好（不用为一个包装类型生成整套代码）
2. **`object` / `JsonElement` 属性会退化** → 不要为了"先跑通"把不确定的字段声明成 `JsonElement`，那等于把 JSON 漏进上层

### 3.2 源生成上下文

```csharp
[JsonSourceGenerationOptions(
    // ★ 一行解决 payInfo.refrain_start 在 music/info 是字符串、在 search 是数字的问题
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(TrackDto))]
[JsonSerializable(typeof(SearchListPayload))]
[JsonSerializable(typeof(CheckRightDto))]
[JsonSerializable(typeof(AudioUrlDto))]
[JsonSerializable(typeof(LoginResultDto))]
[JsonSerializable(typeof(LyricContentDto))]
internal sealed partial class BodianJsonContext : JsonSerializerContext;
```

### 3.3 `PayInfoDto` —— 本层最大的陷阱 ✅

`payInfo` 的字段类型**跨接口不一致**：

| 字段 | `service/music/info` | `search/music/list` |
| --- | --- | --- |
| `refrain_start` / `refrain_end` | **字符串** `"84346"` | **数字** `84346` |
| `limitfree` | **字符串** `"0"` | **数字** `0` |

这个不一致**只在运行时炸，而且只在跑第二个 fixture 时才暴露**。靠 `NumberHandling.AllowReadingFromString` 一行解决，**不要写自定义转换器**。配套必须有一条**双 fixture 对照测试**（两个 fixture 都解析成功且 `RefrainStartMs` 都是 84346），否则 P2 一定在运行时翻车。

字段映射：

| JSON 名 | C# 属性 | 类型 | 备注 |
| --- | --- | --- | --- |
| `cannotDownload` / `cannotOnlinePlay` / `extendAttr` / `paytype` | 同名 | `int` | |
| `limitfree` | `LimitFree` | `int` | **string → int** |
| `refrain_start` / `refrain_end` | `RefrainStartMs` / `RefrainEndMs` | `long` | **string → int**；**单位毫秒** |
| `play` / `down` / `download` | 同名 | `string?` | 4 字符串开关串 |
| `nplay` / `ndown` | 同名 | `string?` | 12 字符串；位序与含义**未验证** |
| `overseas_nplay` / `overseas_ndown` / `listen_fragment` / `local_encrypt` / `tips_intercept` | 同名 | `string?` | |
| `paytagindex` | `PayTagIndex` | `Dictionary<string,int>?` | **不是账号权限表**，是档位名序号表；本项目不做判断 |
| `feeType` | `FeeType` | `Dictionary<string,string>?` | 值也是字符串 `"1"` / `"0"` |
| `freeSign` | `FreeSign` | `string?` | **凭据字段，绝不入日志**；`music/info` 样本里没有，看广告后服务端才下发 |

**判断下载权限的依据是 `download` 与 `cannotDownload`**，不要在客户端自行推断。

### 3.4 `TrackDto`

同时覆盖 `service/music/info` 与各列表接口（两边共有字段名一致，只有"各自独有"的差异）。

| JSON 名 | C# 属性 | 类型 | 来源 |
| --- | --- | --- | --- |
| `id` | `Id` | `long` | 两者 |
| `name` / `songName` | 同名 | `string?` | 两者（同值，留一个兜底） |
| `subtitle` / `FSONGNAME` | 同名 | `string?` | 仅列表 |
| `searchTag` | `SearchTag` | `SearchTagDto?` | 仅列表 —— **是对象不是字符串** |
| `albumId` / `album` / `albumPic` / `albumPic120` | 同名 | `long` / `string?` | 两者 |
| `artist` / `artistId` / `artistPic` / `allArtistId` | 同名 | `string?` / `long` / `string?` / `string?` | 两者 —— `allArtistId` 是**字符串** |
| `artists` | `Artists` | `TrackArtistDto[]?` | 两者 |
| `duration` | `DurationSeconds` | `int` | 两者 —— **秒不是毫秒** |
| `mvduration` | `MvDurationSeconds` | `int` | 仅 info —— 秒 |
| `releaseDate` | `ReleaseDate` | `string?` | 两者 |
| `isMv` / `vid` | 同名 | `int` / `long` | 两者 |
| `online` | `Online` | `int` | 两者 —— **是数字不是 bool** |
| `offline` | `Offline` | `bool` | 仅 info —— **这个是 bool** |
| `preOnline` | `PreOnline` | `bool` | 两者 |
| `isOriginal` / `isNew` / `isShowType` / `isPay` / `tpay` / `downloadAdvert` | 同名 | `int` | 后三个仅 info |
| `psrc` / `intro` / `musicRid` | 同名 | `string?` | 两者 |
| `audios` | `Audios` | `AudioEntryDto[]?` | 两者 |
| `payInfo` | `PayInfo` | `PayInfoDto?` | 两者 |
| `mediaBasicInfo` | `MediaBasicInfo` | `MediaBasicInfoDto?` | 两者（`gain`/`peak`/`lra` 全 `double`） |
| ~~`mvInfo`~~ | —— | **不映射** | ⚠️ 2026-10-04 实测修正：**桌面协议的曲目详情不下放这个键**，`TrackDto` 也没有这个属性。MV 数据要单独调 `service/mv/info`，见 [`mv.md`](mv.md) |
| `lrc_info` | `LrcInfo` | `LrcInfoDto?` | 仅 info —— P4 用它决定请求 `lrcx` 哪一版 |
| `lrcEffect` | `LrcEffect` | `LrcEffectDto?` | 仅 info |
| `lrcUpdateTime` | `LrcUpdateTime` | `string?` | 仅 info |
| `comment` / `favorite` / `share` | 同名 | `long` | 仅 info —— `favorite` 是**全站计数**，不是"我是否收藏" |
| `language` | `Language` | `string?` | 仅 info |
| `categorys` | `Categories` | `CategoryDto[]?` | 仅 info |
| `haveAudition` | `HaveAudition` | `bool` | 仅 info |
| `spectrum` | `Spectrum` | `string?` | 仅 info |

### 3.5 `AudioEntryDto`

| JSON 名 | C# | 备注 |
| --- | --- | --- |
| `level` | `Level` `string` | `s` / `h` / `p` / `ff` / `zp` / `bcms` … |
| `format` | `Format` `string?` | |
| `bitrate` | `Bitrate` `string?` | **字符串**，`"2000"` |
| `size` | `Size` `string?` | **字符串**且带单位，`"52.83Mb"`；`zp` 档是占位串 `"zpMb"` |

两条：**`size` 不要解析成数字**；**数组顺序不按档位高低排**（实测首元素是 `bcms`、`ff` 排第六），7.2 节的选档必须按 `level` 查表，不能靠顺序。

### 3.6 时间单位：同一个响应里三种

| 字段 | 单位 |
| --- | --- |
| 曲目 `duration` / `mvduration` / `audition.duration` / `audition.start` / `audition.end` | **秒** |
| `payInfo.refrain_start` / `refrain_end` | **毫秒** |

按毫秒处理 `duration` 会让每首歌显示成 0:00；按秒处理 `refrain_*` 会让试听片段落到曲末。**所以属性名一律带 `Seconds` / `Ms` 后缀**——让单位错配在调用点就显形，而不是等到界面上看出来。

### 3.7 播放与登录

```csharp
internal sealed class CheckRightDto
{
    [JsonPropertyName("status")]   public int Status { get; init; }   // 3 试听 / 7 无权限 / 其他=完整
    [JsonPropertyName("audition")] public AuditionDto? Audition { get; init; }  // status != 3 时实测不存在
}

internal sealed class AudioUrlDto
{
    [JsonPropertyName("audioUrl")]      public string? AudioUrl { get; init; }
    [JsonPropertyName("audioHttpsUrl")] public string? AudioHttpsUrl { get; init; }
    [JsonPropertyName("format")]        public string? Format { get; init; }      // ★ 服务端实际给的
    [JsonPropertyName("bitrate")]       public int Bitrate { get; init; }         // ★ 这里确实是数字
    // duration(duration 秒) / size(string) / respCode / p2pAudioSourceId 本项目不用
}
```

`AudioUrlDto.Format` 是**服务端实际返回的格式**，与请求的 `format` 可能不同——实测请求 `format=flac` 会被静默降级到 mp3（`fixtures/audiourl-formatflac-downgraded.json`）。**必须校验返回值而不是相信请求参数**，这个 fixture 就是这条回归测试的素材。

`LoginResultDto` 要点：`id` / `bid` / `userInfo.id` 三者一致的校验规则抽成纯函数 `SessionIdentity.Resolve(...)`；**`LoginResultDto.payInfo` 与曲目 `PayInfoDto` 同名不同构，必须是两个类型**，两个类上各写一句 XML 注释互相指认。

### 3.8 P1 做 / 推迟

| 类型 | P1 | 说明 |
| --- | --- | --- |
| `TrackDto` 及全部子对象 | ✅ | 两个 fixture 覆盖，可断言 |
| `PayInfoDto` | ✅ | **双 fixture 对照是本阶段最有价值的 DTO 测试** |
| `SearchListPayload` | ✅ | 分页抽象的落点 |
| `CheckRightDto` / `AuditionDto` / `AudioUrlDto` | ✅ | 四个 fixture 覆盖 |
| `LoginResultDto` 及子对象 | ✅ | 有 fixture（部分字段被脱敏，见 5.3） |
| `LyricContentDto` | ✅ | 只做「一次 Base64」这一层 |
| `PlaylistDto` 及列表 payload | ⏭ P7 | **没有歌单 fixture**，做了无法验证 |
| **评论 DTO** | ✅ P8 | 2026-10-02：v3 列表／回复 payload 与发布／点赞 body 已实现，公开 GET 样本见 `fixtures/comments-*.json`；写请求使用离线替身验证，真实写操作由用户手测。详见 `comments-ui.md` |
| 下载 DTO | ⏭ P9 | 已定论不引入 `download/{info,config,callback}` 三个接口 |
| 歌词解析器与统一模型 | ⏭ P4 | P1 只做入口与系数解码（第 6 节） |

> 这是 P1 的 DTO 取舍记录。评论响应 DTO 已在 P8 用真实 GET 样本补齐；发布与点赞请求来自完整的静态调用链。尚未验证或接入的类型继续在 `Dto/README.md` 标明，不能凭字段名补全。

另外 `LrcInfoDto` 的属性 P1 可以先留，但**消费逻辑**（用它决定请求 `lrcx=1` 还是 `0`）留 P4。

---

## 4. 分页

```csharp
namespace Bodian.Core.Api.Paging;

/// <summary>首页页号各家不同：只有 search/* 从 0，其余全从 1。</summary>
public sealed record PagingConvention(
    string PageParam = "pn",
    string SizeParam = "rn",
    int    FirstPage = 1,
    int    MaxPageSize = 100)
{
    public static readonly PagingConvention ZeroBased = new(FirstPage: 0);
    public static readonly PagingConvention OneBased  = new(FirstPage: 1);

    public int ToPageNumber(int offset, int pageSize) => (offset / pageSize) + FirstPage;
    public int NormalizePageSize(int requested)       => Math.Clamp(requested, 1, MaxPageSize);
}

public sealed class PagedCursor
{
    public PagedCursor(PagingConvention convention, int pageSize = 30);
    public int  Offset         { get; private set; }
    public int  PageSize       { get; }
    public int  PageNumber     { get; }     // 换算后的 pn
    public int  RequestedCount { get; }     // rn
    public bool Exhausted      { get; }
    public void Advance(int receivedCount);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Offset, int PageSize, int? Total);
```

**核心约束**：游标只按「请求页边界」推进，**绝不用返回条数推算下一页偏移**。真实案例：歌单标称 121 首，第一页只返回 99 首——服务端会省略不可用曲目，`total` 与可见歌曲数不保证一致。

所以「继续翻页」的判据是 **`items.Count > 0`**，**不是** `Count == pageSize`（短页不代表到底），也**不是** `offset + count < total`（`total` 本身不可信）。这条要写进 `PagedCursor` 的备注。

> ⚠️ **2026-10-03 更正**：`ZeroBased` 的适用范围比这张表的注释原先写的窄 ——
> `service/album/music/{id}`、`service/artist/music/{id}`、`service/artist/album/{id}`
> 这三个**也是 1 基**（`pn=1` 是第 1 页），只有 `search/*/list` 真的从 0 起。
> 它们传 `pn=0` 不报错、被服务端当第 1 页，所以 0 基在首屏看不出问题，
> 第二次请求（`pn=1`）拿回的还是第 1 页，列表正好翻倍。实测对照表见
> [`bodian-api-reference.md`](bodian-api-reference.md) §1.6。

「各家列表字段名不同」（`resultList` / `list` / `playLists` / `albumList`）的收敛方式：**每个端点族一个具体 DTO**，再用一个内部接口抽出「取列表 + 取总数」：

```csharp
internal interface ITrackListPayload { IReadOnlyList<TrackDto>? Items { get; } int Total { get; } }
internal sealed class SearchListPayload : ITrackListPayload { /* resultList */ }
```

**不能用 `ResultListPayload<T>` 这种开放泛型**——源生成不支持（见 3.1）。P1 只做 `SearchListPayload`，其余 P7 补。

---

## 5. 可测性设计

### 5.1 注册与注入

```csharp
// 生产
services.AddSingleton(sp => BodianHttpTransport.CreateHandler(options));
services.AddSingleton<TimeProvider>(TimeProvider.System);
services.AddSingleton<BodianSession>();
services.AddSingleton<IBodianTransport, BodianHttpTransport>();

// 测试：注入回放 handler
internal sealed class ReplayHandler : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var body = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
        Requests.Add(new(req.Method.Method, req.RequestUri!.OriginalString, body));
        // ...
    }
}
```

关键：断言用 `req.RequestUri!.OriginalString`，**不是** `.Query` 或 `.ToString()`。`OriginalString` 原样返回构造时传入的字符串，不被 `Uri` 规范化，所以能精确断言签名前的 query 串。

### 5.2 fixture 定位

编译期注入（`AssemblyMetadata:RepositoryRoot`，见 1.9）优先，向上遍历兜底：

```csharp
internal static class Fixtures
{
    public static string Root { get; } = Resolve();

    private static string Resolve()
    {
        var fromMetadata = typeof(Fixtures).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryRoot")?.Value;

        if (fromMetadata is { Length: > 0 })
        {
            var dir = Path.GetFullPath(Path.Combine(fromMetadata, "fixtures"));
            if (Directory.Exists(dir)) return dir;
        }

        // 兜底：从输出目录向上找带 docs/ 的目录
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "docs")))
                return Path.Combine(current.FullName, "fixtures");
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("找不到 fixtures/，检查 AssemblyMetadata:RepositoryRoot");
    }
}
```

**不要**用 `<Content CopyToOutputDirectory>` 把 fixture 拷进输出目录——那份副本会悄悄变旧，测试会在过期数据上「通过」。**不要**引 `tools/Bodian.Probe` 作引用——把一个 `net10.0-windows` 的一次性工具变成主工程依赖，等于把 P0 的临时产物固化下来。

### 5.3 脱敏对 fixture 的影响（写进测试注释）

`fixtures/login-users-login.json` 里 `id` / `bid` / `userInfo.id` / `token` / `nickname` / `headImg` 被替换成字符串 `"<redacted>"`。所以：

- **身份一致性规则不能用这份 fixture 测** —— 抽成纯函数 `SessionIdentity.Resolve(long? id, long? bid, long? uid, long? userInfoId, long? userInfoUid)`，用纯值输入测
- fixture 只用来测未被脱敏的部分（`payInfo.isVip == 1`、`userInfo.authType == 3`、`userFreeInfo.freeAdIsFree == 0` 等）
- `audiourl-*.json` / `checkright-*.json` 里的 URL 结尾是 `?<redacted>`。**别拿它测 URL 合法性校验**（会踩 `Uri` 对 `<` `>` 的处理差异），URL 校验用合成输入测

### 5.4 测试清单

**A 组 · 纯逻辑（零网络、零文件）**

| 测试 | 输入 | 断言 |
| --- | --- | --- |
| `FormUrlEncode_WHATWG` | 参数化表 | `" "→"+"`；`"~"→"%7E"`（与 `Uri.EscapeDataString` 的分水岭）；`*` `-` `.` `_` 保留；`"="→"%3D"`、`"&"→"%26"`、`"!"→"%21"`；`"周杰伦"→"%E5%91%A8%E6%9D%B0%E4%BC%A6"`；空串→空串 |
| `Sign_IsOrderIndependent` | 同一组参数洗牌 3 种顺序 | 三次 sign 相同（只有字母数字进 seed，顺序无关——这是真不变量） |
| `Sign_GoldenCheckRight_Characterization` | `sign-golden.json` | `FormUrlEncode(pairs) == SeedQuery` **且** `SignRaw(...) == "bb658ff99a35b3d19570e0d783e9143e"`。**类名与注释必须写明这是 characterization 不是 verified** |
| `GoldenFixture_IsStillMarkedUnverified` | `sign-golden.json` | `verified == false` 且 `reason` 非空 —— 防止有人日后把它当成已验证事实 |
| `Sign_ThroughTransport_Golden` | 冻结 `TimeProvider`=1790750540920 + `ReplayHandler` | 发出的 `OriginalString` 里 query 精确等于黄金值；body 精确等于 `{"musicId":228908,"freeSign":""}`。**这一条同时锁住 query 顺序、编码、盐位置、path 形态、body 参与签名** |
| 信封解析 | `music-info-228908.json` 等 | 200 有 data；`audiourl-anon-20018.json` **没有 data 字段**也能正确取到 code + msg |
| 错误码映射 | 合成 JSON | 每个 code → 正确的 `BodianErrorCode`；未知 code → `Unknown` 且保留原始值 |
| `SessionCleared_On11012` | 合成 11012 | `IsAuthenticated == false`、`Revision` 递增、`Cleared` 触发一次、异常码正确 |
| `AcceptedCodes_11027` | 合成 11027 + `AcceptedCodes=[LoginPending]` | **不抛异常**，信封正常返回 |
| 8 MiB 上限 | handler 返回 9 MiB | 抛限流异常 |
| 超时 | handler 阻塞 + `RequestTimeout=100ms` | `OperationCanceledException`，且不是外层 ct 触发的 |
| handler 配置 | `CreateHandler(options)` | `AutomaticDecompression == All`、`PooledConnectionLifetime == 2min`、`UseProxy` 与 `Proxy` 一致 |
| 非 JSON 响应 | HTML 错误页 | 异常里带原文（已脱敏） |
| 分页换算 | 参数化 | `ZeroBased.ToPageNumber(0,30)=0`、`(30,30)=1`；`OneBased.ToPageNumber(0,30)=1`、`(30,30)=2`；`NormalizePageSize(0)=1`、`(1000)=100` |
| 稀疏分页 | 第 1 页 99 条 / pageSize 100 | `Advance(99)` 后 `Offset == 100`（**不是 99**）；第 2 页空 → `Exhausted` |
| **`PayInfo` 双形态** | `music-info-228908.json` **和** `search-music-list-anon.json` | 两者都解析成功，`RefrainStartMs` 都是 84346 |
| 身份一致性规则 | 纯值输入 | 三者相同 → 采用；任一不同 → 拒绝；只凑出一个 → 拒绝 |
| `LogRedactor` | 含 `token=` / `freeSign=` / `uid=` / `devid=` 的字符串 | 全被替换；**空值不动**（`sign=` 空值是黄金用例的一部分） |
| `RedactingLoggerFactory` | fake `ILoggerProvider` 捕获输出 | 经 `ILogger<T>` 出去的文本已脱敏；`SafeUrl.ToString()` 不泄露 |
| 歌词 | 两份 `.lrc` fixture | 系数标签解析出 **87**（八进制）而不是 127；`lrcx1` 63 行全带逐字、63 行首词 start 全 0、首 5 词 `(0,160)(160,160)(320,160)(480,160)(640,160)`；`lrcx0` 63 行 0 逐字且无标签 |

**B 组 · 需要真实系统，但仍是单元测试**

| 测试 | 说明 |
| --- | --- |
| `DpapiCredentialStore_RoundTrip` | `[WindowsFact]`：写临时路径 → 读回 → 值相等 → 密文里**不含明文 token**。**不碰** `%LOCALAPPDATA%\Bodian` |
| `FileDeviceIdentity_ReusesExisting` | 临时目录：写入合法 devid → `GetOrCreate` 必须**原样返回**（重生成是账号风控信号）；写入非法内容 → 重生成且是 32 位小写十六进制 |

**C 组 · 必须发真实请求的：零。**

gzip 解压是 `SocketsHttpHandler` 的行为，stub handler 在解压链之上，**测不到**。诚实的做法是在「handler 配置」测试里断言 `AutomaticDecompression == All`，把真正的 gzip 验证留到 P2 的首次真实请求。**不要**为了测它去起 `HttpListener` 打本机回环——那测到的是回环而不是 CDN，还破坏了「真实请求为零」。

### 5.5 测试基础设施（`tests/Bodian.Core.Tests/Support/`）

| 文件 | 职责 |
| --- | --- |
| `ReplayHandler.cs` | 回放 + 捕获请求 |
| `Fixtures.cs` | 定位并读取 fixture |
| `FakeTimeProvider.cs` | 冻结时间戳，约 15 行 |
| `WindowsFactAttribute.cs` | 派生于 `FactAttribute`，非 Windows 时设 `Skip` |
| `CapturingLoggerProvider.cs` | 捕获格式化后的日志文本，供脱敏测试断言 |

`InMemoryCredentialStore` 由 Core 提供，不放在测试项目。

---

## 6. 歌词入口（P1 范围）

```csharp
namespace Bodian.Core.Lyrics;

public static class BodianLyricPayload
{
    /// <summary>type=lyric&amp;req=2&amp;lrcx=..&amp;rid=..&amp;... 的完整 payload。</summary>
    public static string BuildRequestPayload(long musicId, int lrcx);

    /// <summary>https://mlyric.kuwo.cn/mobi.s?f=bodian&amp;q=&lt;base64&gt;</summary>
    public static Uri BuildRequestUri(long musicId, int lrcx);

    /// <summary>只做一次 Base64 解码。不要搬老酷我 f=web 的 zlib + yeelion XOR。</summary>
    public static string DecodeContent(string base64Content);
}

public static class KuwoFactorCodec
{
    /// <summary>[kuwo:N] 的 N 是八进制。</summary>
    public static bool TryParse(string lyricText, out int startFactor, out int durationFactor);

    /// <summary>trunc(abs((a+b)/(2*startFactor))) / trunc(abs((a-b)/(2*durationFactor)))。</summary>
    public static (long StartMs, long DurationMs) DecodeWord(long a, long b, int startFactor, int durationFactor);
}
```

P1 只做这三件事 + 5.4 的那几条测试。**AWLRC 解析与统一歌词模型留 P4。**

两条最容易白干的决策用测试钉死：

1. **`[kuwo:N]` 的 N 是八进制**（`parseInt(N, 8)`）。《晴天》的标签是 `[kuwo:127]`，按八进制解出 **87**、factor 8/7，算出干净的 **160ms 网格**；按十进制解出 127、factor 12，会得到非整数、忽快忽慢的时间轴——**而且"看着有值"不像报错**，这是最容易蒙混过关的一处
2. **逐字时间是相对行首的偏移**。fixture 里 63 行的首词 `start` 全为 0，绝对时间不可能每行都从 0 开始

另外：**不要搬老酷我 `f=web` 端点那套** `tp=content` 头剥离 → zlib inflate → Base64 → `yeelion` 循环 XOR 的解密链。波点端点**只需要一次 Base64 解码**（搬错会白干一天）。

`fixtures/lyric-228908-lrcx{0,1}.lrc` 是**已解码的文本**（不是 Base64 原文），可直接断言。

---

## 7. `Bodian.WinUI` 的组合根

P1 的 WinUI 只做**能启动的空窗口 + DI 组合根**，不做导航、页面、ViewModel。

理由：组合根是 P1 唯一「单测覆盖不到、又必须存在」的东西。`ICredentialStore → DPAPI`、`TimeProvider`、`SocketsHttpHandler` 单例、`BodianSession` 单例、**脱敏日志如何套在最外层**——这些只在组合根里存在。不写它，P1 交付的就只是一堆互不相连的类，P2 第一天要重写 `App.xaml.cs`。

而且「0 警告 0 错误 ≠ 能跑」是这个仓库已经踩过的坑（见 `dev-environment.md` 第 4 节的实测记录）。把 Hosting 接进启动路径会让管线变长，风险应该在这一阶段暴露。

```csharp
public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        // ★ 必须在任何窗口创建之前
        NativeMethods.SetCurrentProcessExplicitAppUserModelID("Bodian.WinUI");

        InitializeComponent();
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();                 // 只留 Serilog

        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(AppPaths.LocalAppData, "logs", "bodian-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
        builder.Services.AddSingleton<ILoggerProvider>(new SerilogLoggerProvider(serilog, dispose: true));

        // ★ 脱敏收口：包住整个 ILoggerFactory，任何 provider 都绕不过
        builder.Services.AddSingleton<ILoggerFactory>(sp => new RedactingLoggerFactory(
            new LoggerFactory(sp.GetServices<ILoggerProvider>())));

        builder.Services.AddSingleton(sp => BodianHttpTransport.CreateHandler(
            sp.GetRequiredService<BodianTransportOptions>()));
        builder.Services.AddSingleton<BodianTransportOptions>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IDeviceIdentity, FileDeviceIdentity>();
        builder.Services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
        builder.Services.AddSingleton<BodianSession>();
        builder.Services.AddSingleton<IBodianTransport, BodianHttpTransport>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        _host.Start();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
        => _host.Services.GetRequiredService<MainWindow>().Activate();
}
```

**构造内层工厂时用 `sp.GetServices<ILoggerProvider>()` 显式喂给 `new LoggerFactory(...)`，不要解析 `ILoggerFactory` 自身**——后者会无限递归。

`RedactingLoggerFactory` 是唯一收口，代价是 Serilog 的结构化属性退化成字符串。这个代价要写进注释，并说明为什么接受：`roadmap.md` 明确要求「日志脱敏**做封装，不靠自觉**」，而保住结构化就得靠每个调用点自己记得脱敏。

第二道防线：**transport 根本不构造可泄露的字符串**——只 log `SafeUrl`（scheme + host + path，query 一律丢弃）、`reqId`、方法、耗时、业务码；query 与 body 从头到尾不进入任何格式化字符串。

`MainWindow` 用模板内容 + 标题里放一行 devid。作用：**零网络地证明 DI 链真的通了**——标题上那串来自 `FileDeviceIdentity` 读的 `%LOCALAPPDATA%\Bodian\devid.txt`，必须与 `tools/Bodian.Probe` 用的是**同一个值**。

P1 明确不做：导航服务、页面、ViewModel、`CommunityToolkit.Mvvm`、WinUIEx、AUMID 开始菜单快捷方式（P3）。

---

## 8. 验收

```bash
cd /f/My_Project/bodain_winui

# 1. 整个解决方案 0 警告 0 错误（Core 开了 TreatWarningsAsErrors）
dotnet build Bodian.sln -c Debug

# 2. 红线自检：P0 探针必须仍能独立构建
dotnet build tools/Bodian.Probe -c Debug

# 3. 单测（global.json 已选 MTP）
dotnet test --project tests/Bodian.Core.Tests/Bodian.Core.Tests.csproj
#    失败数必须为 0；跳过数应为 0（本机是 Windows，[WindowsFact] 不该被跳过）
#    备用路径：直接跑 tests/Bodian.Core.Tests/bin/Debug/net10.0/Bodian.Core.Tests.exe

# 4. WinUI 启动（「构建通过 ≠ 能跑」）
timeout 8 ./src/Bodian.WinUI/bin/Debug/net10.0-windows10.0.26100.0/win-x64/Bodian.WinUI.exe; echo "exit=$?"
#    期望 exit=124 —— 跑满 8 秒被 timeout 杀掉 = 窗口一直开着
#    退出码 0 或非零都说明 GUI 提前退出了，有问题
ls ./src/Bodian.WinUI/bin/Debug/net10.0-windows10.0.26100.0/win-x64 | wc -l   # 期望 ~246
tasklist | grep -i bodian || echo "无残留进程"

# 5. 脱敏在真实宿主里生效
#    ★ 必须排除 <redacted> 再判泄露：脱敏后的文本本身也长成 token=<redacted>，
#      直接 grep 'token=...' 会**误报**。
if grep -riE '(token|freeSign|devid|qimei36)=[^&"[:space:]]+' "$LOCALAPPDATA/Bodian/logs/" | grep -v '<redacted>'
then echo "!!! 泄露"; else echo "OK：日志里没有凭据明文"; fi

# 6. devid 与 P0 探针是同一个（启动日志里有一行「设备标识 <32 位小写十六进制>」）
grep -o '设备标识 [0-9a-f]\{32\}' "$LOCALAPPDATA/Bodian/logs/"*.log
cat "$LOCALAPPDATA/Bodian/devid.txt"        # 两者必须相同

# 7. 零改动确认
git status --short                   # tools/Bodian.Probe/ 与 fixtures/ 应无输出
dotnet sln Bodian.sln list           # 恰好 3 个项目
```

**扩展性验证（各跑一次）**：

- `dotnet run --project tools/Bodian.Probe -- whoami` 仍能读到会话 —— 证明 `SessionStore` → `DpapiCredentialStore` 的迁移没破坏凭据格式。**这一条另有单测守着**（`CredentialStoreTests.ReadsProbeSessionFile_WrittenByP0Tool`，文件不存在时跳过）
- **devid 不当作凭据**：它是假名化的设备串，没有它就没有账号访问能力，所以启动日志里记了一行便于核对。换设备标识在账号风控看来是异常信号，值得留痕。`token` / `freeSign` / `sign` 才是入不得日志的

---

## 9. 执行顺序与风险

| 步 | 内容 | 判据 |
| --- | --- | --- |
| P1-a | 骨架（`global.json` / `sln` / props / 3 个 csproj / `.gitignore` / 最小 XAML） | 构建 0/0；探针仍 0/0；WinUI 退出码 124 |
| P1-b | 测试项目跑通（单独成步，见风险表） | `dotnet test` 报 passed |
| P1-c | DTO + 源生成上下文 | 编译 0 警告；双 fixture `payInfo` 测试绿 |
| P1-d | 纯逻辑层（签名 / devid / 凭据） | 编码表测试 + 黄金用例绿 |
| P1-e | 传输层（transport / session / 异常） | A 组测试绿 |
| P1-f | 脱敏层 | 脱敏测试绿 |
| P1-g | 分页 + 门面 | 分页测试绿 |
| P1-h | 歌词入口 | 歌词断言绿 |
| P1-i | WinUI 组合根 | 第 8 节 5 步全绿 |

P1-a 与 P1-b 是所有后续的前置；P1-c / d / f / h 相互独立可并行；P1-i 必须在 P1-e 之后（否则组合根没有东西可注册）。

| 风险 | 预案 |
| --- | --- |
| **`dotnet test` 的 MTP/VSTest 混用**（.NET 10 新机制，且同时引 `xunit.v3` 与 `xunit.runner.visualstudio`） | 单独成步：先只引 `xunit.v3` 跑通，再加 `xunit.runner.visualstudio`，逐次验证。真出问题就丢掉后者（VS 仍可通过 MTP 发现测试）或用第 8 节的备用路径 |
| `payInfo` 双类型是**运行时**才炸 | `AllowReadingFromString` + 双 fixture 对照测试，必写 |
| WinUI 项目无模板，四个属性任一写错就失败 | 照 1.8 的属性集；注意 `tech-stack.md` 第 2 节漏了 `UseWinUI` |
| 源生成的开放泛型限制是**设计期**撞上 | 信封不进 JSON 上下文（3.1 已绕开） |
| 根级 props 打挂探针（NU1008） | 四个文件放 `src/` / `tests/`；第 8 节第 2 步专门自检 |
| `xunit.v3` 传递引入 `Microsoft.Testing.Extensions.Telemetry`，与「遥测不做」形式冲突 | P1-b 确认它默认不上报（兜底 `DOTNET_CLI_TELEMETRY_OPTOUT=1`），结论记入 `dev-environment.md`。该包不进客户端产物，影响面仅限开发机 |
| `RedactingLoggerFactory` 的 DI 自引用 | 用 `GetServices<ILoggerProvider>()`，不解析 `ILoggerFactory`；加一条「容器能构建出 `ILogger<T>`」的冒烟断言 |

**必须写进代码注释、防止后人踩的三件事**：

1. `sign-golden.json` 是 **characterization test，不是已验证事实**（`verified: false`，因为 `ver=1.1.7` 下服务端根本不校验签名）
2. **评论 / 歌单 / 下载 DTO 有意不写**（无 fixture），别照文档字段名凭记忆补全
3. `[kuwo:N]` **必须按八进制解析**（十进制"看着有值"不像报错）

---

## 10. 落地结果与本设计的偏差（2026-09-30 收尾）

P1 已实现并全部验证通过。以下是**与本文初稿不同**的地方，以及原因。

| 项 | 初稿 | 实际落地 | 原因 |
| --- | --- | --- | --- |
| DPAPI 的平台标注 | 「不触发 CA1416」 | **必须标 `[SupportedOSPlatform("windows")]`** | 初稿结论错了，见 2.1 节的更正 |
| `BodianSession.Anonymous` | `static` 共享实例 | **`CreateAnonymous()` 工厂** | 共享的可变单例会让一处登出影响所有使用方，那类 bug 很难查 |
| `BodianHttpTransport` | `internal sealed class` | **`public sealed class`** | DI 注册要用它；改成 `internal` 就得再加一层工厂或 `InternalsVisibleTo("Bodian.WinUI")`，不值得 |
| 信封 | `(Code, Message, RequestId, Data)` | 多一个 **`SessionRevision`** | 让调用方能判断「响应回来时会话是否已经变过」，这是 2.2 节那条机制的落点 |
| 超时异常 | 「抛 `OperationCanceledException`」 | **抛 `TimeoutException`** | 与「调用方主动取消」区分开；后者仍然原样抛 `OperationCanceledException` |
| 移动端签名 | 留 TODO | **留 TODO**（未实现） | 符合已拍板的范围决策；`BodianSigner` 的类注释里写明了为什么 |
| `IBodianApi` 门面 + `Models/` | P1 做 3 个方法 | **推迟到 P2** | 门面要返回 `Models` 里的公开类型，而映射哪些字段取决于 P2 真正要什么；现在做就是猜。**替代品是 `Endpoints.cs`**：只固化已核实的路径常量，不含猜测 |
| 测试项目依赖 | 只列了 xunit 两件套 | 多一个 **`Microsoft.Extensions.Logging`** | 脱敏测试要用 `LoggerFactory.Create` 搭内层工厂，它在非 Abstractions 包里 |

**已实现清单**（全部有单测）：解决方案骨架与三个项目 · DTO 层与源生成上下文 · `BodianSigner` · `FileDeviceIdentity` · `DpapiCredentialStore` / `InMemoryCredentialStore` · `BodianHttpTransport` / `BodianSession` / `BodianErrorCode` / 异常族 · `SafeUrl` / `LogRedactor` / `RedactingLoggerFactory` · `PagingConvention` / `PagedCursor` / `PagedList` · `BodianLyricPayload` / `KuwoFactorCodec` · `Endpoints` · WinUI 组合根。

**验收实测（2026-09-30）**：解决方案 0 警告 0 错误；探针仍能独立构建 0/0；**152 个测试全绿、0 跳过**；WinUI 退出码 124；日志里凭据全被替换；启动日志里的 devid 与 `%LOCALAPPDATA%\Bodian\devid.txt` 逐字相同；探针 `whoami` 仍能读到会话（凭据格式兼容）。
