# libmpv 音频精简构建与维护

**当前 `libmpv-2.dll` 已从 115.22 MiB 精简到 7.97 MiB，减少 93.1%，并替换到客户端输出目录。**
本文是此构建的完整维护说明：查结果、找文件、重新编译、验证、替换、回退和排障都从这里开始。
主 README 的文档索引与 `libmpv/README.md` 均指向本文。

- [当前产物](#当前产物)
- [为什么采用源码裁剪](#为什么采用源码裁剪)
- [保留与裁剪](#保留与裁剪)
- [文件位置](#文件位置)
- [重编译](#重编译)
- [验证与安装](#验证与安装)
- [回退到完整版](#回退到完整版)
- [客户端加载与兼容调整](#客户端加载与兼容调整)
- [构建问题与排查](#构建问题与排查)
- [原完整版来源与回退基线](#原完整版来源与回退基线)
- [维护时更新哪些记录](#维护时更新哪些记录)
- [许可与项目红线](#许可与项目红线)

二进制与构建产物不入版本控制。源码版本与 SHA256 校验和保存在
[`sources.lock.json`](../tools/mpv/sources.lock.json)，构建与验证脚本也随项目维护。

## 当前产物

| 项 | 值 |
| --- | --- |
| 大小 | **8,360,960 字节（7.97 MiB）** |
| SHA256 | `5c9005b0ba3f8b52322d36360c920d82c090c6115cd7ca378d1a409dbc7fbe62` |
| 原社区完整版 | 120,816,640 字节（115.22 MiB） |
| 体积减少 | **93.1%** |
| 架构 | Windows x64，`x86-64` / SSE2 基线，不要求 AVX2 |
| mpv 源码 | `mpv-player/mpv`，commit `e470f8986e`，0.41.0 系列 |
| FFmpeg 源码 | `FFmpeg/FFmpeg`，commit `939c2c733` |
| Client API | `0x20005`，兼容 `HanumanInstitute.LibMpv` 0.10.1 |
| 编译工具链 | LLVM MinGW `20260922`（UCRT x64）+ NASM `2.16.03` |
| 链接方式 | FFmpeg、字体依赖和 C++ 运行时均静态链接；外部导入仅为 Windows 系统库 |

尺寸按 MiB（1,048,576 字节）计算。保留 FFmpeg 的运行时 CPU 检测和汇编优化，
在支持 AVX/AVX2 的机器上仍可选择优化实现，但 DLL 本身不以这些指令集作为最低要求。

mpv 源码归档没有 Git 元数据，因此版本字符串带 `UNKNOWN`；准确来源以固定 commit、
`tools/mpv/sources.lock.json` 和构建清单为准。

## 为什么采用源码裁剪

原社区 DLL 已经剥离调试符号，约 93 MB 的 `.text` 和 20 MB 的只读数据主要来自实际代码
与静态链接依赖。继续 strip 原文件不能解决体积问题；单纯把依赖换成动态 DLL 也不能证明
整个应用体积下降。

客户端将 libmpv 用作 headless 音频引擎，因此本次保留上游 mpv 的客户端 API，重新编译
音频解码、常用容器、网络和 Windows 音频输出，将 FFmpeg 视频解码器及可选依赖裁掉。
LTO、section GC 和最终符号剥离进一步减小新产物。最终 DLL 的外部导入仅为 Windows 系统库。

## 保留与裁剪

保留的核心能力：

- 内置音频解码器，当前实际启用 204 个：MP3、AAC、FLAC、ALAC、Vorbis、Opus、
  PCM、APE、WavPack、WMA、DSD 等。
- 常见音频容器，包括 WAV、AIFF、FLAC、APE、MP3、MP4/M4A、Ogg、ASF、DSF、
  WavPack，以及 HLS 所需的容器。完整清单在 `audio-profile.json`。
- 本地文件、HTTP/HTTPS、代理、TCP/TLS、音频重采样和常用音频滤镜。
  HTTPS 使用 Windows Schannel，免于携带独立 TLS 库。
- WASAPI、音量、暂停/恢复、准确跳转、播放结束事件、缓冲和 gapless-audio。

关闭的能力：

- **全部 FFmpeg 视频解码器**、编码器、复用器和硬件加速器。
- OpenGL、Vulkan、D3D 视频后端，以及 shaderc/glslang 等 GPU 工具链依赖。
- Lua、JavaScript、C 插件、yt-dlp 脚本、光盘和压缩包播放等可选功能。
- mpv 命令行程序和 FFmpeg 命令行程序。

mpv 仍强制依赖 `libass`、`libplacebo`、`libswscale` 和部分视频/字幕核心代码。
这里保留上游结构，用禁用图形后端、静态链接、LTO 和 section GC 缩小体积；
没有通过大量修改上游代码强行删除所有视频相关源文件。

MV 使用 WinUI 的 `MediaPlayer`，不依赖这个 DLL 的视频解码能力。
不在容器白名单中的文件格式需要调整构建配置后重新验证，不能把此构建当成完整的通用 mpv。

## 文件位置

以下路径均相对仓库根目录。**实际使用的库是 `libmpv/libmpv-2.dll`，不是候选构建目录里的文件。**

| 查找内容 | 路径 | 是否入 Git |
| --- | --- | --- |
| 本维护文档 | `docs/libmpv-audio-build.md` | 是 |
| 当前实际使用的 DLL | `libmpv/libmpv-2.dll` | 否 |
| 可复现构建脚本 | `tools/mpv/build_audio_mpv.py` | 是 |
| 原生播放与无损对照脚本 | `tools/mpv/verify_audio_mpv.py` | 是 |
| 固定源码版本与归档校验和 | `tools/mpv/sources.lock.json` | 是 |
| 播放初始化兼容调整 | `src/Bodian.WinUI/Playback/LibMpvPlaybackService.cs` | 是 |
| 候选 DLL | `artifacts/native-mpv/output/libmpv-2.dll` | 否 |
| 构建清单 | `artifacts/native-mpv/output/build-manifest.json` | 否 |
| 实际启用组件清单 | `artifacts/native-mpv/output/audio-profile.json` | 否 |
| 完整版回退备份 | `artifacts/native-mpv/reference/libmpv-2.dll` | 否 |
| 播放与 PCM 验证报告 | `artifacts/native-mpv/verification-signals/verification-report.json` | 否 |
| 合成音频样本 | `artifacts/native-mpv/verification-signals/fixtures/` | 否 |
| 各阶段配置与编译日志 | `artifacts/native-mpv/logs/` | 否 |
| 下载的源码归档 | `artifacts/native-mpv/downloads/` | 否 |
| 隔离的工具链与 Python 环境 | `artifacts/native-mpv/bootstrap/`、`artifacts/native-mpv/venv/` | 否 |
| Debug 客户端输出 DLL | `src/Bodian.WinUI/bin/Debug/net10.0-windows10.0.26100.0/win-x64/libmpv-2.dll` | 否 |

`artifacts/` 是本机可再生目录，重新克隆仓库不会得到这里的备份、工具链或验证报告。
固定来源与本次验收结果已写入本文；换机器时按下面的构建和参考库获取步骤重新生成。

## 重编译

要求 Windows x64、Python 3.12+、Git for Windows。使用英文且没有空格的工作目录。
所有下载、私有 Python 环境、临时文件和构建结果默认放在仓库的 `artifacts/native-mpv/`，
不需要修改系统 PATH 或安装到系统目录。

在仓库根目录执行：

```powershell
python tools/mpv/build_audio_mpv.py --jobs 8
```

脚本固定并校验工具链和全部源码归档的 SHA256，不会替换 `libmpv/libmpv-2.dll`。
首次构建需要下载约 190 MB 的编译工具链，以及源码和私有 Python 构建工具。

Git Bash 不在默认位置时传入：

```powershell
python tools/mpv/build_audio_mpv.py --bash E:/Git/usr/bin/bash.exe
```

已有构建目录中某一步失败，可从该阶段恢复，例如：

```powershell
python tools/mpv/build_audio_mpv.py --resume-from mpv
```

`--bootstrap-lock` 只供维护者首次建立或审查源码校验和；日常构建不应使用。
新配置生成的 DLL 校验和可能不同，更新文档前应重新完成播放验证。

构建产物：

| 路径 | 内容 |
| --- | --- |
| `artifacts/native-mpv/output/libmpv-2.dll` | 候选 DLL |
| `artifacts/native-mpv/output/build-manifest.json` | 体积、SHA256、工具链和源码版本 |
| `artifacts/native-mpv/output/audio-profile.json` | 实际启用的解码器、容器、滤镜和协议 |
| `artifacts/native-mpv/logs/` | 各阶段配置、编译和外部导入日志 |

## 验证与安装

验证使用完整版 mpv 编码合成测试音频，不涉及私人音乐文件。原版备份在：

```text
artifacts/native-mpv/reference/libmpv-2.dll
```

如果新克隆的仓库没有备份，需要自行获取下面记录的完整社区构建，再通过 `--reference`
指定它的位置。精简 DLL 没有编码器，不能用它生成测试样本。

```powershell
python tools/mpv/verify_audio_mpv.py --reference artifacts/native-mpv/reference/libmpv-2.dll
```

验证脚本默认访问一个公开的 HTTPS MP3 样本；可用 `--https-url` 指定另一个可播放且
允许跳转的 HTTPS 音频地址。WASAPI 验证需要可用的 Windows 音频输出设备，全程静音。
报告写入 `artifacts/native-mpv/verification-signals/verification-report.json`。

当前产物已通过：

- 17 个播放用例：WAV 16/24-bit、FLAC、MP3、AAC/M4A、ALAC/M4A、Vorbis/Ogg、
  Opus、WavPack、WMA、24-bit/96 kHz 与 192 kHz 的 WAV/FLAC、HTTP FLAC、HTTPS MP3、WASAPI。
- 每个用例验证暂停、恢复、音量、跳转、进度事件和自然播放结束。
- 7 组无损 PCM 对照逐字节一致，包括 24-bit/96 kHz 与 192 kHz 的 FLAC。
- 项目使用的 `HanumanInstitute.LibMpv` 0.10.1 托管绑定加载、属性、命令和事件验证。
- WinUI 项目编译，以及外部导入仅含 Windows 系统库的检查。

验证通过并保留旧版本备份后，才将候选 DLL 复制到 `libmpv/libmpv-2.dll`，然后重新构建客户端。
需要回退时可用 `Copy-Item` 把完整版备份复制回来，保留备份本身。

### 安装已验证的候选库

下列命令均在仓库根目录执行。先确认验证脚本成功退出，且报告中的 SHA256 与候选 DLL 一致：

```powershell
$candidatePath = 'artifacts/native-mpv/output/libmpv-2.dll'
$reportPath = 'artifacts/native-mpv/verification-signals/verification-report.json'
$verificationReport = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$candidateHash = (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($candidateHash -ne $verificationReport.sha256) {
    throw '候选 DLL 与验证报告不一致，请重新验证。'
}
```

本次替换前已将原 DLL 复制到 `artifacts/native-mpv/reference/libmpv-2.dll`，并核对其 SHA256。
以后升级候选库时，在覆盖当前版本前再单独保留当前 DLL；不要覆盖唯一的完整版参考库。

```powershell
$currentPath = 'libmpv/libmpv-2.dll'
$previousHash = (Get-FileHash -LiteralPath $currentPath -Algorithm SHA256).Hash.ToLowerInvariant()
$previousDir = Join-Path 'artifacts/native-mpv/backups' $previousHash
$null = New-Item -ItemType Directory -Force -Path $previousDir
$previousPath = Join-Path $previousDir 'libmpv-2.dll'
if (-not (Test-Path -LiteralPath $previousPath)) {
    Copy-Item -LiteralPath $currentPath -Destination $previousPath
}
if ((Get-FileHash -LiteralPath $previousPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $previousHash) {
    throw '旧版本备份校验失败，请勿替换。'
}
Copy-Item -LiteralPath $candidatePath -Destination $currentPath -Force
if ((Get-FileHash -LiteralPath $currentPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $candidateHash) {
    throw '安装后的 DLL 校验失败。'
}
dotnet build src/Bodian.WinUI/Bodian.WinUI.csproj --no-restore -c Debug -v minimal
```

构建后核对输出目录的 DLL。构建或复制失败时不要跳过错误继续使用旧输出；若运行中的客户端
占用了输出目录的 DLL，关闭客户端再重试。

```powershell
$outputPath = 'src/Bodian.WinUI/bin/Debug/net10.0-windows10.0.26100.0/win-x64/libmpv-2.dll'
$outputHash = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($outputHash -ne $candidateHash) {
    throw '客户端输出目录尚未使用已验证的候选 DLL。'
}
```

### 本次构建验收记录

本次 `dotnet build` 成功，0 个错误，1 条已有的 `AiPlaylistPage.xaml` / `WMC1506` 绑定警告。
实际使用目录和 Debug 输出目录中的 DLL 均为 8,360,960 字节，SHA256 均与当前产物表一致。
C# 验证使用 `HanumanInstitute.LibMpv` 0.10.1，实测 Client API 为 `0x20005`，完成 FLAC 加载、
属性读写、跳转、播放事件和停止操作。

## 回退到完整版

先核对完整版备份的哈希，再复制回库目录并重新构建。备份本身保留不动：

```powershell
$referencePath = 'artifacts/native-mpv/reference/libmpv-2.dll'
$referenceHash = '0d5b9dbecb73e179ef39dc94314ff8905fab926a80f8d60a2367d824b7254e2e'
if ((Get-FileHash -LiteralPath $referencePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $referenceHash) {
    throw '完整版参考库的 SHA256 不一致，请勿回退。'
}
Copy-Item -LiteralPath $referencePath -Destination 'libmpv/libmpv-2.dll' -Force
dotnet build src/Bodian.WinUI/Bodian.WinUI.csproj --no-restore -c Debug -v minimal
$outputPath = 'src/Bodian.WinUI/bin/Debug/net10.0-windows10.0.26100.0/win-x64/libmpv-2.dll'
if ((Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $referenceHash) {
    throw '客户端输出目录尚未更新为完整版。'
}
```

回退后若要重新使用精简版，按上一节安装已验证候选库，再确认运行目录的 SHA256。

## 客户端加载与兼容调整

[`Bodian.WinUI.csproj`](../src/Bodian.WinUI/Bodian.WinUI.csproj) 把此 DLL 复制到输出目录；托管绑定从 `MpvApi.RootPath`
（`AppContext.BaseDirectory`）加载它。缺文件时仍可构建和启动，首次播放会显示可读错误。

关闭 Lua 后不存在 `ytdl` 选项，因此 [`LibMpvPlaybackService`](../src/Bodian.WinUI/Playback/LibMpvPlaybackService.cs) 会检测 `options/ytdl`
是否存在后再关闭该脚本，兼容完整版与音频精简版。

## 构建问题与排查

| 问题 | 本次采用的处理 | 相关位置 |
| --- | --- | --- |
| 原 DLL 已剥离符号，strip 后仍约 115 MiB | 从固定上游源码重编译，裁掉视频解码器和可选依赖 | `build_audio_mpv.py`、`audio-profile.json` |
| 本机旧 MinGW 无法作为稳定构建基线 | 下载固定 LLVM MinGW 到隔离目录，进程内设置 PATH | `bootstrap/`、`sources.lock.json` |
| NASM 报错无法打开中文用户目录下的临时文件 | 将构建进程的 `TEMP`、`TMP`、`TMPDIR` 指向英文工作路径中的 `tmp/` | `build_audio_mpv.py` 的 bootstrap 阶段 |
| 禁用 Vulkan 后仍缺少 `vulkan/vulkan.h` | `libplacebo` 的 stub 仍需要类型声明，补齐 Vulkan-Headers；后端继续禁用 | 固定的 `vulkan-headers` 源码条目 |
| FFmpeg 安装头文件时报引号未闭合 | 原生 GNU make 的日志命令过长，安装阶段使用 `V=1` 缩短命令 | `ffmpeg-install.log` |
| mpv 报 OpenGL 已启用但找不到输出后端 | `gl` 上游默认值为 enabled，需要显式 `-Dgl=disabled` | `mpv-configure.log` |
| 链接缺少 `std::to_chars`、`__gxx_personality_seh0` 等符号 | 最终链接使用 C++ driver 并静态链接 C++ 运行时 | `mpv-compile.log`、`imports.log` |
| 关闭 Lua 后播放器初始化失败 | `ytdl` 选项已不存在，先检测 `options/ytdl` 再设置 | `LibMpvPlaybackService.EnsureInitialized` |
| 验证脚本无法编码测试样本 | `--reference` 必须使用完整版；精简版本没有编码器 | `verify_audio_mpv.py` |
| WASAPI 验证失败 | 检查 Windows 音频输出设备是否可用；该用例全程静音 | `verification-report.json`、运行时错误 |
| 提示归档 SHA256 不一致 | 保留异常归档供检查，在新的 `--work-dir` 重新下载；不要绕过哈希校验 | `downloads/`、`bootstrap/` |

各阶段日志默认写入 `artifacts/native-mpv/logs/<阶段>.log`。解决错误后可以在同一工作目录
使用 `--resume-from <阶段>`，但前置阶段必须已经成功完成；更换源码版本或工具链时使用
新的工作目录，不复用旧依赖来判断新配置是否可用。

## 原完整版来源与回退基线

| 项 | 值 |
| --- | --- |
| 构建仓库 | `shinchiro/mpv-winbuild-cmake` |
| Tag | `20260928` |
| 资产 | `mpv-dev-x86_64-20260928-git-e470f8986e.7z`（31,490,080 字节） |
| 归档 SHA256 | `81795d759e01016f1550fd71651a1a5d59ab5c28ef31c0b6793224e9cff39459` |
| DLL SHA256 | `0d5b9dbecb73e179ef39dc94314ff8905fab926a80f8d60a2367d824b7254e2e` |
| DLL 大小 | 120,816,640 字节 |

原版已经 strip 过，体积主要来自真实代码和静态链接依赖。本次减重来自重编译和组件裁剪，
不是继续 strip 原文件，也没有通过新增外部 DLL 转移体积。

### 换机器时获取完整版参考库

要求已安装 7-Zip，并且 `7z` 可以在当前终端执行。固定归档用于生成合成验证样本或回退；
不要用官方客户端里的库代替它。

```powershell
$referenceDir = 'artifacts/native-mpv/reference'
$null = New-Item -ItemType Directory -Force -Path $referenceDir
$archivePath = Join-Path $referenceDir 'mpv-dev-x86_64-20260928-git-e470f8986e.7z'
$archiveUrl = 'https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20260928/mpv-dev-x86_64-20260928-git-e470f8986e.7z'
Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath
$archiveHash = '81795d759e01016f1550fd71651a1a5d59ab5c28ef31c0b6793224e9cff39459'
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archiveHash) {
    throw '完整版归档 SHA256 不一致，请勿解压使用。'
}
7z e $archivePath 'libmpv-2.dll' "-o$referenceDir" -aos
if ($LASTEXITCODE -ne 0) { throw '完整版 DLL 解压失败。' }
$referencePath = Join-Path $referenceDir 'libmpv-2.dll'
$referenceHash = '0d5b9dbecb73e179ef39dc94314ff8905fab926a80f8d60a2367d824b7254e2e'
if ((Get-FileHash -LiteralPath $referencePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $referenceHash) {
    throw '完整版 DLL SHA256 不一致。'
}
```

`-aos` 跳过已存在的文件，避免覆盖已有参考库；最后的哈希检查确认它仍是约定的完整版。
归档保留在本机，后续需要时可以再次提取。

## 维护时更新哪些记录

修改解码器、容器、滤镜或协议白名单时，先确认服务端实际音源和本地文件的需求，再调整
`build_audio_mpv.py`。升级依赖时，审查固定版本及下载归档的 SHA256，再更新
`sources.lock.json`；不要用 `--bootstrap-lock` 绕过已知版本的校验失败。

每次替换实际库前完成以下记录：

1. 构建成功，保存新的 `build-manifest.json`、`audio-profile.json` 和阶段日志。
2. 重新运行完整播放与无损对照验证，候选 DLL 哈希必须与报告一致。
3. 验证项目的托管绑定和 WinUI 构建，检查实际输出目录也使用同一 DLL。
4. 在本文更新产物大小、SHA256、固定版本、Client API、保留范围和验收结果。
5. 归档旧 DLL，完成替换；Git 仅提交文档、脚本、锁定文件及必要的播放代码调整。

主 README、技术栈文档和库目录 README 保留索引或简短结论；详细命令、哈希和排查记录
统一在本文维护，避免不同文件中的操作说明逐渐不一致。

## 许可与项目红线

本项目沿用 GPL-3.0，mpv 精简构建保留 `gpl=true`。分发时仍需提供对应源码和构建材料；
`tools/mpv/` 记录配置与固定源码，下载的原始源码归档保存在 `artifacts/native-mpv/downloads/`。
原始社区完整版沿用其 GPLv2+ 分发条件。

**不要分发官方 PC 客户端里的 `E:/bodian/libmpv-2.dll`。**
本次编译只使用上游开源源码，不使用官方客户端二进制。
