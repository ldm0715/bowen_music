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

## 3. 两个曾修复的配置问题（换机器时重点看这节）

这两个都是**把 VS 装在非默认盘（`F:`）留下的坑**，与项目本身无关，但会让构建直接失败。

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

构建产物在 `bin\Debug\net10.0-windows10.0.26100.0\win-x64\`，**246 个文件**（含 WinUI 原生 DLL）是自包含正常的标志。

```
timeout 8 ./probe.exe; echo $?
```

**退出码 124 = 跑满 8 秒被 timeout 终止 = 启动正常。** 退出码 0 或非零都说明 GUI 应用提前退出了，有问题。这个方法可以脚本化地判断「窗口是否一直开着」。

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
