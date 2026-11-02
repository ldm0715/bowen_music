# libmpv 原生库

本目录存放客户端实际使用的 `libmpv-2.dll`。**二进制已入库**（约 8.07 MiB）：
它是运行时必需文件，缺了它客户端能启动但播放没声音，构建本身不会报错。
当前采用项目的音频精简构建，包含经典/增强/环境音需要的音频滤镜。

**完整维护文档：[libmpv 音频精简构建与维护](../docs/libmpv-audio-build.md)。**

当前版本：

- 大小：8,457,728 字节（8.07 MiB）
- SHA256：`ef27c3f0700bf29a09adf5a04343f4d5a9a3a5e7e64b2303ab196a1652be3c9f`
- 构建：2026-11-01，GitHub Actions `build-libmpv.yml`
- 旧版备份：`artifacts/native-mpv/backup/5c9005b0ba3f8b52322d36360c920d82c090c6115cd7ca378d1a409dbc7fbe62/libmpv-2.dll`（本机，不入 Git）

精简范围、固定源码与校验和、重编译、播放验证、替换与回退、构建问题均集中在该文档。
构建与验证脚本位于 `tools/mpv/`；原版完整库需按文档自行获取。
