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
              info <musicId>             第 2 步：公开接口 + 请求头自检
              checkright <musicId>       第 3 步：checkRight（GET + JSON body + 签名）
              signtest <musicId>         签名对照：六种形态逐一试，锁定返回 200 的那一种
              call <path> [k=v ...]      直接调任意接口

            选项
              --post                     用 POST（默认 GET）
              --signed                   带 timestamp 与 sign
              --body <json>              JSON body，原样参与签名并发送
              --name <name>              存 fixture 时的文件名（call 命令）
              --save                     把脱敏后的响应存进 fixtures/
              --proxy <url>              代理，如 http://127.0.0.1:7890
              --verbose                  在 stderr 打印实际请求 URL 与 body
              -h, --help                 本帮助

            示例
              bodian-probe devid
              bodian-probe info 10250281307392909
              bodian-probe signtest 10250281307392909
              bodian-probe checkright 10250281307392909 --save

            环境变量
              BODIAN_PROXY               等价于 --proxy
            """);
    }
}
