# libmpv 原生库

本目录存放客户端的音频引擎原生依赖 `libmpv-2.dll`。**二进制不入版本控制**
（`.gitignore` 排除），本文件记录来源与复现步骤。

## 来源

| 项 | 值 |
| --- | --- |
| 构建仓库 | [`shinchiro/mpv-winbuild-cmake`](https://github.com/shinchiro/mpv-winbuild-cmake)（社区构建，mpv 官方不提供 Windows 构建） |
| Tag | `20260928` |
| 资产 | `mpv-dev-x86_64-20260928-git-e470f8986e.7z`（31,490,080 字节） |
| 归档 sha256 | `81795d759e01016f1550fd71651a1a5d59ab5c28ef31c0b6793224e9cff39459` |
| **`libmpv-2.dll` sha256** | `0d5b9dbecb73e179ef39dc94314ff8905fab926a80f8d60a2367d824b7254e2e` |
| 大小 | 120,816,640 字节 |
| mpv 版本 | 稳定版 **v0.41.0** |
| 架构 | x64（标准版，非 `-v3`） |

用标准版而非 `x86_64-v3`：v3 构建要求 CPU 支持 AVX2，在老机器上会直接加载失败。

## 复现步骤

```bash
cd libmpv
curl -L -O "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20260928/mpv-dev-x86_64-20260928-git-e470f8986e.7z"
sha256sum mpv-dev.7z          # 应等于上表的归档 sha256
7z e mpv-dev.7z libmpv-2.dll -y
rm mpv-dev.7z
sha256sum libmpv-2.dll        # 应等于上表的 dll sha256
```

## 许可

mpv 本体是 LGPLv2.1+，但 shinchiro 的默认构建启用了 GPL-only 组件，**整包按 GPLv2+ 分发**。
本项目是 GPL-3.0，GPLv2+ 的「或更高版本」允许按 GPLv3 使用，直接用即可。

分发本 dll 时需履行 GPL 的对应源码提供义务；构建脚本与补丁在
`shinchiro/mpv-winbuild-cmake` 仓库的对应 tag 上。

## 红线

**不要分发官方 PC 客户端里的 `libmpv-2.dll`**（本机路径 `E:\bodian\libmpv-2.dll`）。
那条与许可无关，是"不打包官方客户端任何二进制"的项目红线。

## 客户端如何找到它

`Bodian.WinUI.csproj` 把本目录的 dll 拷到输出目录，`HanumanInstitute.LibMpv` 的
`WindowsFunctionResolver` 从 `MpvApi.RootPath`（默认 `AppContext.BaseDirectory`）
加载 `libmpv-2.dll`。**缺文件时构建不报错**，由 `LibMpvPlaybackService` 在首次播放时
给出可读的错误提示——这样新克隆的仓库不做这一步也能正常构建与启动。

## 为什么是 115 MB（不用试图 strip）

它的段分布是：

| 段 | 大小 | 说明 |
| --- | --- | --- |
| `.text` | 93 MB | 真实代码 |
| `.rdata` | 20 MB | 只读数据 |
| 其余全部 | < 2 MB | —— |

**没有 `.debug_*` 段，这个 dll 本来就是 strip 过的**（实测：`strip` 与 `strip --strip-all`
跑完文件大小一字节不变，CPU 时间接近 0）。体积来自**静态链接的 ffmpeg 与全部解码器**。

对比：官方 PC 客户端的 `libmpv-2.dll` 是 29.7 MB，那是**动态链接**版本——省下的体积
转嫁到了同目录的另一批 dll 上，整包并不更小。

所以不要去 strip，也不要为了体积换构建。真要减体积只能自己编译一个裁掉无关解码器的版本，
维护成本远超收益。分发时整包压缩会回到约 31 MB。

