# 打包与发布

**核查日期：2026-10-08。** 本文的体积、文件数均为本机实测（`dotnet publish -c Release -r win-x64`，
Windows 10 Pro 19045，.NET SDK 10.0.400）。体积按 MiB（1,048,576 字节）计算，
下载页上的 MB 按 1 MB = 1,000,000 字节算，两者刻意分开写。

- [三个产物](#三个产物)
- [版本号只有一个来源](#版本号只有一个来源)
- [发布流程](#发布流程)
- [本地复现](#本地复现)
- [安装器的设计取舍](#安装器的设计取舍)
- [changes.md 的约定](#changesmd-的约定)
- [首次发版前的准备](#首次发版前的准备)
- [★ 尚未验证的部分](#-尚未验证的部分)

## 三个产物

| 产物 | 变体 | 压缩包 | 解压后 | 文件数 | 目标机器需要 |
| --- | --- | --- | --- | --- | --- |
| `BowenMusic_x.y.z_x64_setup.exe` | 自包含 | 约 48 MB | 189.8 MiB | 340 | 无 |
| `BowenMusic_x.y.z_x64_portable.zip` | 自包含 | 69.6 MiB | 189.8 MiB | 340 | 无 |
| `BowenMusic_x.y.z_x64_noruntime_portable.zip` | 框架依赖 | **37.0 MiB** | 113.3 MiB | 153 | **.NET 10 运行时（x64）** |

三者内容完全一样，差别只在要不要把 .NET 装进包里 —— 打包命令只差 `--self-contained` 一个开关。

**安装包只做自包含**。安装版是给「双击就想要能用」的人用的，装完才发现缺运行时是最糟的失败方式；
想要小体积下载的人走 `noruntime` 便携版，那条路上缺运行时的预期是明确的。

**`noruntime` 要的是 .NET 运行时，不是桌面运行时。** 实测它的
`Bodian.WinUI.runtimeconfig.json` 只声明了 `Microsoft.NETCore.App 10.0.0`，
没有 `Microsoft.WindowsDesktop.App`（WinUI 3 不用 WPF/WinForms 那套）。
所以让用户装最小的那个「.NET Runtime 10」就够，不必拉几百 MB 的桌面运行时。

> 与 `size-optimization.md` 的数字对不上是正常的：那份记的是 **`dotnet build` 的输出目录**
> （115.2 MiB / 217 个文件），这里是 **`dotnet publish`**。publish 会剔掉一批只在开发期
> 用得到的文件，所以文件数从 217 掉到 153。**对外以本文为准。**

## 版本号只有一个来源

`src/Directory.Build.props` 里的 `<Version>` 是**唯一来源**（关于页显示的也是它，
经 `Assembly.GetName().Version` 读取）。发布时 tag 反过来去对账这个值：

```
tag v0.1.0  ←→  <Version>0.1.0</Version>
```

工作流里「版本号对账」那一步不一致就直接失败。**这一步不能省**：否则会出现
「关于页写 0.1.0、Release 标 v0.2.0、应用检查更新时又认为自己已经是最新」这种
只能靠人肉发现的偏差，而用户看到的是错误的版本号。

发布时版本号由 tag 传给 MSBuild（`-p:Version=`），所以**不需要在发版前手改
`Directory.Build.props`** —— 但**要改**，理由是：本地构建与 CI 构建应当是同一条路，
改了 prop 才能让 `-p:Version` 与 prop 一致，对账那步也才有意义。

## 发布流程

打 tag 即发布：

```bash
git tag v0.1.0
git push origin v0.1.0
```

[`.github/workflows/release.yml`](../.github/workflows/release.yml) 收到 tag 后依次做：

| 步骤 | 说明 |
| --- | --- |
| 版本号对账 | tag 与 `Directory.Build.props` 必须一致，且形如 `v<主>.<次>.<修订>` |
| 检查改动记录 | `changes.md` 里必须有该版本的段落，缺了直接失败 |
| 离线测试 | `dotnet test`，当前 1594 项，**零真实网络请求** |
| 发布两个变体 | 自包含 + 框架依赖，都带 `-p:DistributionBuild=true` |
| 校验产物完整性 | 逐个确认 `libmpv-2.dll` / `exe` / `dll` / 图标在位，且没混进 `onnxruntime.dll`、没留 pdb |
| 打便携 zip | `7z a -tzip -mx=9`，条目落在压缩包根目录（不多一层 `publish\`） |
| 打安装包 | `choco install nsis` 后跑 `installer/BowenMusic.nsi` |
| 组装发布说明 | 从 `changes.md` 抽出该版本段落，把 `<!--CHECKSUMS-->` 换成实测 SHA-256 表格 |
| 建 Release | `gh release create` 上传三个资产；tag 已存在 Release 时改为 `gh release edit` + `gh release upload --clobber`（**可重复跑**） |

也可以手动触发（`workflow_dispatch`），填一个**已经存在**的 tag 重跑 —— 调试首发时用得上。

### 为什么完整性校验那一步是必需的

`Bodian.WinUI.csproj` 里 `libmpv-2.dll` 是**条件拷贝**：

```xml
<None Include="$(LibMpvDir)libmpv-2.dll" Condition="Exists(...)" ... />
```

文件不在就静默跳过，**构建照样成功**，只会得到一个「能启动、一按播放就报缺库」的包。
这是刻意的设计（新克隆不做那一步也能 0/0 构建），所以流水线必须自己验一遍，
不能靠「构建成功」推断「包是完整的」。

## 本地复现

CI 跑的就是下面这些，本机可以完整复现（`gh` 那步除外）：

```powershell
# 1. 测试
dotnet test --project tests/Bodian.Core.Tests/Bodian.Core.Tests.csproj -c Release

# 2. 两个变体
dotnet publish src/Bodian.WinUI/Bodian.WinUI.csproj -c Release -r win-x64 `
  --self-contained true  -p:DistributionBuild=true -o artifacts/release/publish
dotnet publish src/Bodian.WinUI/Bodian.WinUI.csproj -c Release -r win-x64 `
  --self-contained false -p:DistributionBuild=true -o artifacts/release/publish-noruntime

# 3. 便携 zip（在 publish 目录里打，条目才不会多一层目录）
Push-Location artifacts/release/publish
7z a -tzip -mx=9 ..\BowenMusic_0.1.0_x64_portable.zip *
Pop-Location

# 4. 安装包
choco install nsis -y
$root = (Resolve-Path '.').Path
& 'C:\Program Files (x86)\NSIS\makensis.exe' `
  '/DAPP_VERSION=0.1.0' '/DAPP_VERSION4=0.1.0.0' `
  "/DAPP_SOURCE=$((Resolve-Path artifacts/release/publish).Path)" `
  "/DOUTFILE=$((Resolve-Path artifacts/release).Path)\BowenMusic_0.1.0_x64_setup.exe" `
  "/DAPP_LICENSE=$root\LICENSE" `
  "/DAPP_ICON=$root\src\Bodian.WinUI\Assets\Ripple.ico" `
  installer/BowenMusic.nsi
```

`makensis` 的前三个变量**必传**，缺了直接编译失败 —— 不会静默产出一个指向空目录的安装包。
后两个（`APP_LICENSE` / `APP_ICON`）不传时会退回相对脚本目录的写法，本地一般够用；
CI 传绝对路径，不依赖 makensis 的调用目录。

## 安装器的设计取舍

脚本在 [`installer/BowenMusic.nsi`](../installer/BowenMusic.nsi)。

**per-user 安装，不触发 UAC。** 装到 `%LOCALAPPDATA%\Programs\BowenMusic`。
应用自己的数据也在 `%LOCALAPPDATA%\Bowen` 下，与安装位置同级，语义一致；
用户数据的读写本来就全在用户上下文里，没必要为了 Program Files 抬权限。

**开始菜单快捷方式由安装器创建，AUMID 由应用补上。** 两边写的是同一个文件
（`%APPDATA%\Microsoft\Windows\Start Menu\Programs\波纹音乐.lnk`），不会出现两条重名项：

1. 安装器 `CreateShortCut` 建一条普通快捷方式 —— 装完立刻能从开始菜单找到并启动；
2. 应用启动时 `StartMenuShortcutInstaller.EnsureInstalled` 读到它，发现**缺 AUMID**，
   重写成带 `Bodian.WinUI` AUMID 的那条（`IsUpToDate` 同时比对目标路径与 AUMID）。

第 2 步是必要的：NSIS 的 `CreateShortCut` 写不了 `PKEY_AppUserModel_ID`，
而 SMTC 面板要靠它把进程和快捷方式对上 —— 缺了面板上显示的就是 `Bodian.WinUI.exe` 这个名字。

**安装前会先关掉正在运行的实例。** 判据用 `tasklist ... /FO CSV /NH` 的输出而不是默认表格：
默认输出的「没有匹配任务」提示随系统语言变，按它判断会在中文 Windows 上失效。
关之前会问一次，不会直接杀进程。

**卸载默认保留用户数据。** 卸载时单独问一次「是否同时删除 `%LOCALAPPDATA%\Bowen`」，
默认选「否」—— 卸载重装是常见操作，登录凭据、播放记录、歌单缓存删掉就找不回来了。

**安装目录用 ASCII 名**（`BowenMusic`），显示名才是「波纹音乐」。中文路径本身没问题，
但装到非中文用户名的机器上、或用户手动指定路径时，ASCII 目录名少一类要排查的情况。

## changes.md 的约定

[`changes.md`](../changes.md) 是**发布说明的唯一来源**：Release 正文就是把它里面
`## <版本号>` 那一段原样抽出来的，所以只要维护这一个文件，不用在 GitHub 网页上再抄一遍。

两条硬约定：

1. 版本标题必须写成 `## 0.1.0 — 2026-10-08`（版本号与 tag 去掉 `v` 一致）——
   对不上就抽不出段落，工作流会直接失败；
2. 每个版本的 `### 校验` 下面是占位标记 `<!--CHECKSUMS-->`，工作流把它换成三个资产的
   SHA-256 表格。**不要手工删掉它，也不要手工填校验和。**

格式沿用项目既有的更新记录写法：一段导语 + `### 新增` / `### 更新` / `### 修复`。

## 首次发版前的准备

1. 仓库已建在 `https://github.com/ldm0715/bowen_music`，`AppUpdateOptions.Default`
   已指向它（`Owner` / `Repository` / `ProjectUrl`）—— 这是更新源的**唯一配置点**。
2. 本仓库还没有配 remote，需要：

   ```bash
   git remote add origin https://github.com/ldm0715/bowen_music.git
   git push -u origin main
   ```

3. 确认仓库的 Actions 已启用。工作流里已声明 `permissions: contents: write`，
   建 Release 与传资产靠它，不需要另外配 secret（用的是内置的 `GITHUB_TOKEN`）。
4. 打 tag 前把 `changes.md` 里当前版本的段落补齐，把 `Directory.Build.props` 的
   `<Version>` 改成同一个值。

## ★ 尚未验证的部分

**这套流水线还没有跑过。** 以下几条是已知的不确定项，第一次跑要盯一下：

| 项 | 风险与判据 |
| --- | --- |
| **NSIS 脚本没有本机编译过** | 本机没装 NSIS，脚本未经过 `makensis` 实测。风险集中在 MUI2 页面宏与 `!insertmacro` 的顺序上；CI 第一次跑要先看「打安装包」这一步 |
| Windows SDK `10.0.26100` | TFM 是 `net10.0-windows10.0.26100.0`，代理镜像必须带这个 SDK。工作流里有一道「环境自检」会把它列出来 |
| `actions/setup-dotnet` 能否解析 `10.0.x` | 与 `global.json` 里固定的 `10.0.400`（`rollForward: latestFeature`）要对得上 |
| `choco install nsis` 是否落在那条路径 | 工作流按 `C:\Program Files (x86)\NSIS` 找 `makensis.exe`，找不到会明确报错而不是静默跳过 |
| 安装包实际体积 | 表里写的「约 48 MB」是按 7z 的 LZMA2 结果（46.9 MiB）估的，NSIS 的 LZMA solid 与它接近但不相等。**第一次跑完把真实值回填到本文与 README** |
| 装完能不能跑 | 打包成功不等于能跑。装完在**干净的机器或新用户**上过一遍：启动 → 登录 → 播放 → 逐字歌词 → 托盘 → 单实例 → 卸载（并确认用户数据按预期保留/删除） |
