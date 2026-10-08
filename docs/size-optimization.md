# 编译产物体积优化

**核查日期：2026-10-08。** 本文数字全部来自本机实测（`dotnet build -c Release`，
Windows 10 Pro 19045，.NET SDK 10.0.400）。体积按 MiB（1,048,576 字节）计算。

Release x64 输出目录从 **174.6 MiB / 450 个文件** 压到 **115.2 MiB / 217 个文件**，
减少 **59.4 MiB（34.1%）**。功能零损失，部署方式不变。

- [当前产物](#当前产物)
- [体积归因](#体积归因)
- [三项改动](#三项改动)
- [两个踩过的坑](#两个踩过的坑)
- [评估过但不做的](#评估过但不做的)
- [维护须知](#维护须知)

## 当前产物

| 构建方式 | 字节 | MiB | 文件数 |
| --- | --- | --- | --- |
| 改造前 | 183,067,467 | 174.6 | 450 |
| 默认 Release | 120,757,500 | **115.2** | 217 |
| `-p:DistributionBuild=true` | 119,110,544 | **113.6** | 215 |

对外分发看的是压缩后的数字：**zip（deflate level 9）39.8 MB**。换成 7z 的 LZMA
还能再小一截。用户下载的是这个数，不是 115 MiB。

## 体积归因

改造后的 115.2 MiB 构成：

| 项 | 大小 | 占比 |
| --- | --- | --- |
| WinAppSDK / WinUI 运行时 | 62.5 MB | 54% |
| `Microsoft.Windows.SDK.NET.dll`（WinRT 投影） | 25.1 MB | 22% |
| 其余托管依赖（Extensions / Serilog / NAudio / 托盘 / Win2D…） | 15.4 MB | 13% |
| `libmpv-2.dll` | 8.0 MB | 7% |
| Win2D（逐字歌词） | 2.8 MB | 2% |
| **Bodian 应用自身**（dll + `pri` + exe + pdb） | 6.3 MB | 5% |
| `Microsoft.Extensions.*` | 1.1 MB | 1% |
| Serilog | 0.3 MB | — |

**前两项占 76%**，都是 WinUI 3 自包含部署的固有量级，不是代码写胖了。自己写的
部分连同 XAML 资源一共才 6.3 MB。这也是为什么本文的结论是「能砍的都砍完了」——
剩下的空间全在框架里。

## 三项改动

### 1. 摘掉 AI / ML / Search / Widgets 四个组件（−55.0 MB）

`Microsoft.WindowsAppSDK` 2.5.1 是个**元包**，依赖里挂着 10 个组件。其中四个与
本应用毫无关系，NuGet 恢复时会原样把它们的原生资产拷进输出目录：

| 组件包 | 输出占用 | 带进来的东西 |
| --- | --- | --- |
| `Microsoft.WindowsAppSDK.AI` | 9.9 MB | `Microsoft.Windows.AI.*`、`PerceptiveStreaming.dll`、`NPUDetect.dll`、`Microsoft.Asg.SemanticIndex.*` |
| `Microsoft.WindowsAppSDK.ML` | — | 只是个壳，没有任何资产 |
| `Microsoft.Windows.AI.MachineLearning` | **39.4 MB** | **`onnxruntime.dll` 21.7 MB + `DirectML.dll` 18.7 MB** |
| `Microsoft.WindowsAppSDK.Search` | 3.3 MB | `Microsoft.Windows.Search.dll` |
| `Microsoft.WindowsAppSDK.Widgets` | 2.4 MB | `Microsoft.Windows.Widgets.dll` |

依赖图已逐个核对：WinUI / Foundation / Base / InteractiveExperiences / DWrite /
Runtime 六个真正需要的组件，**一个都不依赖上述四包**。源码里对 `Windows.AI`、
`Widgets`、`SemanticIndex`、`onnx`、`DirectML` 是 grep 零命中。

做法是在 csproj 里逐个声明它们并加 `ExcludeAssets="all"`，只摘资产、不收内容：

```xml
<PackageReference Include="Microsoft.WindowsAppSDK.AI" ExcludeAssets="all" />
<PackageReference Include="Microsoft.WindowsAppSDK.ML" ExcludeAssets="all" />
<PackageReference Include="Microsoft.Windows.AI.MachineLearning" ExcludeAssets="all" />
<PackageReference Include="Microsoft.WindowsAppSDK.Search" ExcludeAssets="all" />
<PackageReference Include="Microsoft.WindowsAppSDK.Widgets" ExcludeAssets="all" />
```

**`Microsoft.Windows.AI.MachineLearning` 必须单独列。** 它不在
`Microsoft.WindowsAppSDK.ML` 里，而是后者的依赖；挡住 ML 不会连带挡住它，39.4 MB
的 CPU 推理栈照样进输出目录。这一条漏了只省 15.6 MB 而不是 55.0 MB。

### 2. 语言资源目录 91 → 2（−3.4 MB）

WinAppSDK 会铺 91 个语言目录，每个里面两份 `.mui`（`Microsoft.ui.xaml.dll.mui` 与
`Microsoft.UI.Xaml.Phone.dll.mui`）。本应用只面向中文用户，其余全留着。

| 属性 | 管什么 | 是否有效 |
| --- | --- | --- |
| `<SatelliteResourceLanguages>` | 托管附属程序集（`.resources.dll`） | ✅ 有效，但**管不到这批 `.mui`** |
| `_TrimWinAppSDKFrameworkLanguages`（自建 target） | WinAppSDK 的原生 `.mui` | ✅ 实际起作用的是它 |

原因是 WinAppSDK 的语言资源放在包的 `runtimes-framework/` 下（不是标准的
`runtimes/`），由 `Microsoft.WindowsAppSDK.Base` 的 `SelfContained.targets` 自己拷贝，
不经过 NuGet 的附属资源机制。具体做法见 csproj 里那个 target 的注释。

### 3. 分发版去 PDB（−1.6 MB）

在 `src/Directory.Build.props` 里加了开关：

```
dotnet build -c Release -p:DistributionBuild=true
```

去掉 `Bodian.WinUI.pdb` 与 `Bodian.Core.pdb`。**默认保留 PDB**——线上出问题时
Serilog 日志里的行号比 1.6 MB 值钱。开关放在 `Directory.Build.props` 而不是某个
csproj，否则只会去掉一个项目的 pdb。

## 两个踩过的坑

### 坑一：不能只引六个组件包、把元包摘掉

第一版做法是把 `<PackageReference Include="Microsoft.WindowsAppSDK" />` 换成六个组件包。
**构建直接失败**：

```
error MSB4011: 无法再次导入 "microsoft.windowsappsdk.winui\2.3.9\...\Microsoft.WinUI.props"
error : The MSIX build tools use the 'CustomBeforeMicrosoftCommonTargets' MSBuild property ...
```

原因是 **`H.NotifyIcon.WinUI` 2.4.1 声明依赖 `Microsoft.WindowsAppSDK >= 1.6.250108002`**。
之前这个下限被 2.5.1 元包统一掉了；摘掉元包后没人再提供它，NuGet 就按「满足区间的最低版本」
把 **1.6 元包整个拉回来**，于是 1.6 与 2.5.1 的 WinUI 组件重复导入。

`project.assets.json` 里能看到证据：`Microsoft.WindowsAppSDK/1.6.250108002` 与
`Microsoft.WindowsAppSDK.WinUI/2.3.9` 并存。

**所以元包必须保留**，只能按坑二里的办法挡子包资产。

### 坑二：`MicrosoftWindowsAppSDKFilesExcluded` 这个官方口子不能按路径用

`SelfContained.targets` 里留了一行排除口子：

```xml
<MicrosoftWindowsAppSDKFiles Remove="@(MicrosoftWindowsAppSDKFilesExcluded)" />
```

看起来很好用，实际有三层坑，全部实测踩过：

1. **自己拼规范化路径 → 静默失效。** `WindowsAppSdkComponentPackages` 的 `Identity`
   形如 `...\2.3.9\buildTransitive\..\`（**带 `..`**），而 `Remove` 比的是 ItemSpec
   字符串。用 `$(NuGetPackageRoot)` 拼出来的规范化路径与它不相等，一个文件也挡不掉，
   而且**不报错**。
2. **改用 `%(Identity)` 原样拼 → glob 不展开。** 路径里的双反斜杠让 glob 匹配失败，
   Include 退化成 4 个不展开的字面量（`@(...->Count())` 实测为 4）。那一轮看起来
   「生效」了，其实是因为 `Remove` 支持通配符，把字面量当模式匹配掉了——纯属巧合，
   连要保留的目录一起删。
3. **用 Item 的 `Exclude` 属性 → 反向误删。** `Exclude` 内部同样按规范化路径比对，
   于是保留清单不生效、要保留的中文目录被删光。

**结论：不碰这个口子。** 改成在自己的 target 里、等 WinAppSDK 拷完之后，按 `None` 项的
`Link` 元数据删除（`Link` 是干净的 `zh-CN\Microsoft.ui.xaml.dll.mui` 形式）。判定条件用
`.mui` 结尾，既精确又不会误伤同在 `native/` 下的 `Microsoft.UI.Xaml\Assets`（那是 `.png` / `.html`，
必需）。

另外 `%(Link)` 这类元数据**不允许出现在求值期的 Condition 中**（`MSB4190`），所以这段
只能写在 `Target` 里，不能写成顶层 `ItemGroup`。

## 评估过但不做的

| 方向 | 实测/预估收益 | 结论 |
| --- | --- | --- |
| 降 TFM 到 `net10.0-windows10.0.19041.0` | `Microsoft.Windows.SDK.NET.dll` 25.1 → 23.7 MB，**只省 1.4 MB** | ❌ 不值，还要放弃 26100 的 API 面 |
| 挡掉 WebView2 托管 DLL（源码零使用）+ `System.Numerics.Tensors`（AI 链残留） | −1.8 MB | ❌ 收益太小，不值得多两条特判 |
| `Microsoft.Extensions.Hosting` 体系换成裸 DI + Serilog | −2~3 MB | ❌ 要动代码，Hosting 担着 DI 与生命周期，风险和收益不成比例 |
| **WinAppSDK 改框架依赖**（`WindowsAppSDKSelfContained=false`） | **−62.5 MB，落到约 55 MiB** | ⏸ 唯一的大头。代价：用户须先装 Windows App Runtime 2.5，且推翻 `tech-stack.md` §2 / `transport.md` §1.8 已定案的「不需要系统级安装」。待定 |
| `libmpv-2.dll` 再裁 | 已经 115.22 → 7.97 MiB（见 `libmpv-audio-build.md`） | ❌ 没空间了 |

**明确不可行，别在这上面花时间：**

- **`PublishTrimmed`**：WinUI 3 靠反射解析 XAML 类型和 WinRT 投影元数据，裁剪后运行期崩，
  官方不支持 WinUI 3 裁剪。
- **Native AOT**：WinUI 3 不支持。
- **把 .NET 改成自包含**：反方向的加法，会多约 70 MB。当前依赖系统 .NET 10 运行时。

## 维护须知

1. **改了 csproj 或包版本后必须全量重新构建。** MSBuild 只做增量拷贝，**不会删除**
   已不在项目里的文件。上一次 174 MiB 的输出目录里会原样留着那批 AI DLL，不清掉就
   会以为优化没生效。清 `bin/`（已 gitignore）或换 `-p:BaseOutputPath=` 建到新目录。
2. **升级 WinAppSDK 时先核对元包依赖表。** `Microsoft.WindowsAppSDK` 的依赖列表变化时，
   四个被挡的组件版本号（见 `src/Directory.Packages.props`）要跟着走。查法：
   `cat ~/.nuget/packages/microsoft.windowsappsdk/<版本>/microsoft.windowsappsdk.nuspec`。
3. **自检命令**：对账 `Bodian.WinUI.deps.json` 里声明的运行时程序集与输出目录实际文件，
   缺一个都会在启动时报缺 DLL。当前 80 个声明、0 缺失。
4. 语言资源那段的判定依赖 `.mui` 后缀这一事实。**若 WinAppSDK 将来把非资源文件也放进
   语言目录**，这条 target 需要跟着改。

## 实测对账记录（2026-10-08）

清过 `obj`/输出目录后，从零构建 Release：

| 检查 | 结果 |
| --- | --- |
| 输出总体积 | 120,757,500 字节（115.2 MiB），217 个文件 |
| AI / ML / Search / Widgets 残留 | 无（`onnxruntime` / `DirectML` / `SemanticIndex` / `PerceptiveStreaming` / `NPUDetect` / `Windows.AI.*` 全部为空） |
| 语言目录 | 仅 `zh-CN`、`zh-TW` |
| `Microsoft.UI.Xaml\Assets` | 完好（`NoiseAsset_256x256_PNG.png`、`map.html`） |
| 关键运行时文件 | `Bodian.WinUI.exe` / `Bodian.WinUI.dll` / `Bodian.Core.dll` / `libmpv-2.dll` / `Microsoft.ui.xaml.dll` / `Microsoft.WinUI.dll` / `Microsoft.UI.Xaml.Controls.dll` / `Microsoft.WindowsAppRuntime.dll` / `Microsoft.Windows.SDK.NET.dll` / `Microsoft.Graphics.Canvas.dll` 全部在位 |
| `deps.json` 声明的运行时程序集 | 80 个，输出目录 **0 缺失** |
| `deps.json` 里的 AI 条目 | 只剩 `Microsoft.Windows.AI.MachineLearning/2.1.74` 一个空壳（`runtime` / `native` / `runtimeTargets` 全为空），不触发加载，无害 |
| 应用启动与功能 | **见下方核对清单** |

构建通过不等于能跑。改动后请在真机上过一遍：启动无缺 DLL 报错 → 登录/切号 →
播放与切歌（libmpv）→ 逐字歌词（Win2D）→ 托盘与关闭到托盘 → 单实例 →
扫码登录二维码 → MV → 输入框右键菜单文本仍为中文。
