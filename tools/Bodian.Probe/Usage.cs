namespace Bodian.Probe;

internal static class Usage
{
    public static void Print()
    {
        Console.WriteLine("""
            bodian-probe —— 波点协议 P0 探针（一次性工具，不进主工程）

            用法： bodian-probe <命令> [参数...] [选项]

            命令
              devid                      生成或读取设备标识（32 位小写十六进制，持久化复用）
              login                      第 4 步：扫码登录（用小号）
              whoami                     显示当前会话
              logout                     删除本地会话

              info <musicId>             第 2 步：公开接口 + 请求头自检
              checkright <musicId>       第 3 步：checkRight（GET + JSON body + 签名）
              play <musicId>             第 5 步：取音源，ffprobe 校验 + ffplay 解码
              lyric <musicId>            第 7 步：取歌词，核对逐字解码的三处存疑
              signtest <musicId>         签名对照：对照请求 + 六种形态，判断接口到底看不看签名
              sigsweep <musicId>         签名算法变体扫描（只在 --ver 触发强制校验时才有意义）
              call <path> [k=v ...]      直接调任意接口
              uploadpic <歌单id> <文件>  上传歌单封面（multipart，字段名 file）

            除 devid / login / logout 外，命令一律带已保存的会话执行。

            选项
              --post                     用 POST（默认 GET）
              --delete                   用 DELETE（歌单删除一类的端点）
              --put                      用 PUT（歌单编辑 service/playlist）
              --sign-body-bytes          uploadpic 用：把二进制字节按 Latin-1 进签名
                                         （默认不签 body，用来验证服务端到底看不看）
              --signed                   带 timestamp 与 sign
              --body <json>              JSON body，原样参与签名并发送
              --name <name>              存 fixture 时的文件名（call 命令）
              --save                     把脱敏后的响应存进 fixtures/
              --timeout <秒>             login 的等待上限（默认 300）
              --quality <档位>           play 用：standard / high / lossless / hires（默认 lossless）
              --seconds <秒>             play 用：ffplay 解码时长（默认 20）
              --no-play                  play 用：只 ffprobe，不解码
              --br <值>                  play 用：绕过档位表直接指定 br，探任意档位
              --lrcx <0|1>               lyric 用：1 逐字版（默认）/ 0 逐行版
              --ver <版本>               覆盖 ver 请求头（默认 1.1.7）。
                                         ≤3.0.0 服务端不校验签名，≥3.5 强制校验并返回 439
              --force-sign <hex>         用指定的 sign 发送，绕过本地计算（签名调试用）
              --plat <值>                覆盖 plat 请求头（默认 win）。移动端专属端点要传 android
              --mobile-sign              用移动端签名（签整条 URL）而不是桌面签名。
                                         与 --signed 同用；见 reverse/findings/03-sign-mobile.md
              --proxy <url>              代理，如 http://127.0.0.1:7890
              --verbose                  在 stderr 打印实际请求 URL 与 body
              -h, --help                 本帮助

            示例
              bodian-probe devid
              bodian-probe login
              bodian-probe signtest 228908
              bodian-probe play 228908 --quality hires

            环境变量
              BODIAN_PROXY               等价于 --proxy
            """);
    }
}
