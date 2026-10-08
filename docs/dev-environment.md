# 波点 WinUI 3 客户端 · 开发环境

**本文记录的是本机实测状态（2026-09-30），不是通用安装指南。** 换机器或重装时对照本文排查，重点看「非默认路径」和「两个曾修复的配置问题」两节——那是最容易浪费时间的地方。

---

## 1. 三个非默认路径（先记这个）

| 东西 | 实际位置 | 默认位置 |
| --- | --- | --- |
| **Visual Studio 2026** | `F:\visual_studio\APP` | `C:\Program Files\Microsoft Visual Studio\` |
| **Windows SDK 10.0.26100.0** | `F:\Windows Kits\10` | `C:\Program Files (x86)\Windows Kits\10` |
| **ffmpeg / ffplay / ffprobe** | `E:\ffmpeg\bin` | 无默认，手动装 |

- Windows SDK 的根目录记录在注册表 `HKLM\SOFTWARE\WOW6432Node\Microsoft\Microsoft SDKs\Windows\v10.0` 的 `InstallationFolder`。**在 `C:\Program Files (x86)\Windows Kits` 下找不到它是正常的**，别以为没装。
- ffmpeg 是手动解压安装的，所以 **`winget list` 里查不到**，只能靠 `which ffmpeg` 确认。

---

## 2. 已具备（无需安装）

| 项 | 版本 / 状态 |
| --- | --- |
| Visual Studio | Community **2026**，18.10.12106.202 |
| VS 工作负载 | `CoreEditor`、`NativeDesktop`（C++ 桌面）、**`ManagedDesktop`（.NET 桌面开发）** |
| VS WinUI 组件 | `Microsoft.VisualStudio.Component.WindowsAppSdkSupport.CSharp` |
| Windows SDK | **10.0.26100.0**，完整（含 `um\Windows.h` 与 `ucrt`） |
| .NET SDK | **10.0.400**（默认）、10.0.112、9.0.101、8.0.404 |
| Git | 2.45.2.windows.1 |
| ffmpeg | `E:\ffmpeg\bin`，2023-04-10 gyan.dev full build |
| winget | 1.29.380 |
| 开发者模式 | **已开启**（`AllowDevelopmentWithoutDevLicense = 0x1`） |
| 代理 | 7890 端口 |
| NuGet 源 | `nuget.org`（默认，无需改） |
| 架构 | AMD64 |

> **不要装 Windows App SDK Runtime。** 项目选了 `WindowsAppSDKSelfContained=true`，运行时随应用一起分发，不需要系统级安装。

---

## 3. 两个曾修复的配置问题 + 四条反直觉事实（换机器时重点看这节）

3.1 与 3.2 都是**把 VS 装在非默认盘（`F:`）留下的坑**，与项目本身无关，但会让构建直接失败。
3.3 是另一类——不是配置坏了，是 .NET 10 工具链的行为与直觉相反，照着老习惯敲命令会直接卡住。

### 3.1 NuGet fallback 指向了不存在的盘

```
C:\Program Files (x86)\NuGet\Config\Microsoft.VisualStudio.FallbackLocation.config
```

原内容指向 `D:\Visual_Studio\TOOL\NuGetPackages`——那是 VS 还在 `D:` 时的路径。**后果是任何需要从网上拉包的 restore 都会失败**：

```
error NU1301: 本地源"D:\Visual_Studio\TOOL\NuGetPackages"不存在。
```

注意这个错误**用 `--source` 或 `-p:RestoreFallbackFolders=` 都覆盖不掉**，只能在配置层解决。

**已改为** `F:\visual_studio\TOOL\NuGetPackages`（该目录确实存在，里面有 `microsoft.windows.sdk.buildtools`）。

**免提权的替代修法**：在那个文件改不动时，可在用户级配置 `%APPDATA%\NuGet\NuGet.Config` 里加一段清掉它（配置分层合并，用户级优先于机器级）：

```xml
<fallbackPackageFolders>
  <clear />
</fallbackPackageFolders>
```

> 该文件开头标注 `auto-generated`，由 VS 安装器生成。**VS 大版本升级后可能被重新生成**，届时留意盘符是否正确。

### 3.2 系统 PATH 里 x86 的 dotnet 排在 x64 前面

`C:\Program Files (x86)\dotnet\` 里是**只有运行时、没有 `sdk` 目录**的安装。32 位宿主看不到 x64 的 SDK，于是终端里敲 `dotnet` 直接报：

```
No .NET SDKs were found.
```

**已把 `C:\Program Files\dotnet` 调整到 x86 那条之前。** 这项需要管理员权限，且改完要**重开终端**才生效。

### 3.3 四条反直觉事实（**2026-09-30 实测**，P1 开工时核实）

| # | 事实 | 证据 | 后果 |
| --- | --- | --- | --- |
| 1 | **`dotnet new sln` 默认生成 `.slnx`**（XML 格式），不是 `.sln` | `dotnet new sln --help` 的 `-f, --format <sln\|slnx>` 写着 `默认: slnx` | 不写 `-f sln` 就得到 `Bodian.slnx`，VS / `dotnet sln add` 的行为都要重新确认 |
| 2 | **WinUI 3 没有 CLI 模板** | `dotnet new list winui` 找不到匹配（退出码 103）；全表里只有 console / classlib / winforms / wpf / xunit 等；`Microsoft.WindowsAppSDK.Templates` 在 nuget.org 上不存在 | WinUI 项目的 csproj 与 XAML **只能手写**，没有捷径可抄 |
| 3 | **`xunit.v3` 要求测试项目 `OutputType=Exe`** | 包的 `buildTransitive` targets 里有 `Condition=" '$(OutputType)' != 'Exe' "` 的 `<Error>`；`dotnet new xunit` 生成的是 **xunit v2（2.9.3）**，不符合选型 | 测试 csproj 必须手写，且 `OutputType` 不能沿用 classlib 的默认值 |
| 4 | **`dotnet test` 要走 MTP 必须在 `global.json` 里显式选择** | `dotnet test --help` 首行：「若要使用 Microsoft.Testing.Platform，请通过 global.json 选择加入」 | 不写 `{"test":{"runner":"Microsoft.Testing.Platform"}}` 就退回 VSTest 模式；选了 MTP 之后调用形态变为 `dotnet test --project <csproj>`，不再接受位置参数 |

**另一个容易打挂 P0 探针的坑**：`Directory.Build.props` / `Directory.Packages.props` 是**从项目目录向上逐级查找**的。放仓库根会让 `tools/Bodian.Probe` 继承 `ManagePackageVersionsCentrally`，它那两行带 `Version=` 的 `PackageReference` 直接报 **NU1008**。**这两个文件放 `src/` 和 `tests/`，仓库根保持干净。**

### 3.4 测试宿主的遥测（**已排除，2026-09-30**）

`xunit.v3` 会传递引入 `Microsoft.Testing.Extensions.Telemetry`（2.4.0）。它是**默认开启、靠环境变量退出**的：

```
TESTINGPLATFORM_TELEMETRY_OPTOUT=1    或    DOTNET_CLI_TELEMETRY_OPTOUT=1
```

（变量名取自 `Microsoft.Testing.Platform.dll` 内的提示字符串，非记忆。）

这与 README 的「不含遥测与日志上报」冲突，而「设环境变量」属于「靠自觉」。**已在 `tests/Bodian.Core.Tests/Bodian.Core.Tests.csproj` 里用 `ExcludeAssets="all"` 直接把它挤出运行时**：

```xml
<PackageReference Include="Microsoft.Testing.Extensions.Telemetry" ExcludeAssets="all" PrivateAssets="all" />
```

> **注意**：写成 `<PackageReference Update="..." ExcludeAssets="all" />` **不生效**——对传递引入的包用 `Update` 改 `ExcludeAssets` 不会移除已解析的资产，DLL 照样被拷进输出目录（实测）。必须用 `Include` 提升为直接引用。因为开了中央包管理，还要在 `src/Directory.Packages.props` 里补一条 `PackageVersion`。

**效果**：输出目录里不再有 `Microsoft.Testing.Extensions.Telemetry.dll`，`deps.json` 只剩不含 runtime 资产的库条目；测试仍正常通过，且单次运行从 ~1.8s 降到 ~0.7s。

该包只影响开发机的测试宿主，不进客户端产物。

### 3.5 测试里不要用 `Progress<T>` 当记录器（**2026-10-05**）

`Progress<T>` 把回调投递到**当前同步上下文**；测试里没有上下文，于是退化成往线程池排队。
原来的写法是「`await Task.Yield()` 之后断言收齐了几条上报」——那等于赌线程池先执行谁：

```csharp
var reports = new List<BatchProgress>();
await _service.SetLikedManyAsync(ids, progress: new Progress<BatchProgress>(reports.Add), cancellationToken: Ct);
await Task.Yield();
Assert.Equal(3, reports.Count);     // ← 偶发得到 2
```

实测 `LikedSongsServiceTests.SetLikedMany_ReportsProgressAsItGoes` **三次里挂一次**；
`PlaylistMusicWriterTests.ReportsProgressAfterEachTrack` 是同一套写法，只是还没撞上。

**改用 `tests/Bodian.Core.Tests/Support/RecordingProgress.cs`**：`Report` 直接同步调用，
断言在 `await` 返回后立刻成立。

**这不是产品代码的问题** —— 界面上的 `Progress<T>` 是在 UI 线程建的，投递回 UI 线程正是它该有的行为。
只有测试里需要「记下来」这种同步语义。

完整的项目文件与约束见 [`transport.md`](transport.md) 第 1 节。

---

## 4. 环境验证方法

**构建通过不等于能跑**——unpackaged + self-contained 的 bootstrapper 初始化是最容易出问题的一环。真正的验收要分两步。

### 4.1 构建

最小 WinUI 3 项目（关键属性见 `tech-stack.md` 第 2 节，**注意 `RuntimeIdentifier` 不可省**）：

```
dotnet new console -o probe
# 改 TFM 为 net10.0-windows10.0.26100.0，加 UseWinUI / WindowsPackageType=None
#   / WindowsAppSDKSelfContained=true / RuntimeIdentifier=win-x64
dotnet build
```

### 4.2 启动

构建产物在 `bin\Debug\net10.0-windows10.0.26100.0\win-x64\`。**文件数量会随依赖变化，不能用它判定 .NET 是否自包含**：早期探针是 246 个文件，2026-10-08 体积优化后 Release 为 217 个文件 / 115.2 MiB。

**改过 csproj 或包版本后必须全量重新构建。** MSBuild 只做增量拷贝，**不会删除**已不在项目里的文件：优化前那批 AI DLL 会原样留在旧输出目录里，不清掉就会以为没生效。清 `bin/`（已 gitignore）或换 `-p:BaseOutputPath=` 建到新目录；细节见 [`size-optimization.md`](size-optimization.md)。

**Windows App SDK 与 .NET 的自包含是两回事。** 项目设置了 `WindowsAppSDKSelfContained=true`，但没有设置 .NET 的 `SelfContained=true`。当前 `Bodian.WinUI.runtimeconfig.json` 使用 `framework: Microsoft.NETCore.App 10.0.0`，启动时需要系统的 x64 .NET 10 运行时；2026-10-06 已验证实际加载 `C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.12\coreclr.dll`。

**不要混用旧构建产物。** 本次遇到两类实际故障：依赖系统 .NET 的新配置与旧 `hostfxr.dll` 并存，启动器转而在应用目录查找框架，误报缺少 .NET；旧 XAML 文件与新程序集混用，则导致绑定或类型转换异常。处理方式是备份旧 `obj` 和输出目录，再使用正常构建命令完整重新生成，不向旧目录拼接不同构建的文件，也不因此重装系统 .NET。

```
timeout 8 ./probe.exe; echo $?
```

**退出码 124 只表示进程在 8 秒后仍未退出。** 安装运行时提示框也可能让进程持续运行，因此还需确认「波点音乐」主窗口正常响应、实际加载了预期 .NET 运行时，且本次 PID 没有致命启动日志。已有实例时，第二次启动会正常退出并唤起原窗口，不能把这种退出码当成启动失败。

### 4.3 本次实测结论（2026-09-30）

| 检查 | 结果 |
| --- | --- |
| `dotnet` 解析 | `C:\Program Files\dotnet`（x64） |
| WinUI 3 构建 | WinAppSDK **2.5.1** + `net10.0-windows10.0.26100.0` + unpackaged + self-contained，0 警告 0 错误 |
| WinUI 3 启动 | 退出码 124，窗口正常运行，无残留进程 |

**结论：整条工具链可用，可以开工。**

探针项目建完即删，不进仓库。

---

## 5. P0 阶段会用到

| 用途 | 工具 | 状态 |
| --- | --- | --- |
| 验证音源可播（`docs/roadmap.md` P0 第 5 步） | `ffplay` | 已有，`E:\ffmpeg\bin` |
| 抓包 / 对比请求 | 待定 | P0 用控制台 + `HttpClient` 即可，不必装抓包工具 |
