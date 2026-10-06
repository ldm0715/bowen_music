# libmpv 原生库

本目录存放客户端实际使用的 `libmpv-2.dll`，二进制不入版本控制。
当前采用项目的音频精简构建：**7.97 MiB，比原版 115.22 MiB 减少 93.1%**。

**完整维护文档：[libmpv 音频精简构建与维护](../docs/libmpv-audio-build.md)。**

精简范围、固定源码与校验和、重编译、播放验证、替换与回退、构建问题均集中在该文档。
构建与验证脚本位于 `tools/mpv/`；当前 DLL 位于本目录；原版备份位于
`artifacts/native-mpv/reference/libmpv-2.dll`。
