using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bodian.Probe;

internal static class Commands
{
    private const string InfoPath = "service/music/info";
    private const string CheckRightPath = "play/music/v2/checkRight";
    private const string AudioUrlPath = "play/music/v2/audioUrl";

    // ── devid ────────────────────────────────────────────────────────────────

    public static int Devid()
    {
        var devid = DeviceIdentity.GetOrCreate();

        Console.WriteLine($"devid：{devid}");
        Console.WriteLine($"位置：{DeviceIdentity.StoragePath}");
        Console.WriteLine($"状态：{(DeviceIdentity.IsNewlyCreated ? "本次新建" : "复用已有")}");
        return 0;
    }

    // ── 第 2 步：公开接口自检 ────────────────────────────────────────────────

    public static async Task<int> InfoAsync(ProbeClient client, string musicId, bool save)
    {
        if (!IsValidMusicId(musicId))
        {
            Console.Error.WriteLine($"musicId 非法：{musicId}（要求 ^\\d{{1,20}}$ 且不全为 0）");
            return 2;
        }

        var response = await client.SendAsync(InfoPath, [new("musicId", musicId)]);
        Console.WriteLine(response.Describe());
        SaveIfRequested(save, $"music-info-{musicId}", response);

        if (response.Code != 200)
        {
            PrintBody(response, raw: false);
            return 1;
        }

        PrintTrack(response.Data);
        return 0;
    }

    // ── 第 3 步：checkRight ─────────────────────────────────────────────────

    public static async Task<int> CheckRightAsync(ProbeClient client, string musicId, string freeSign, bool save)
    {
        if (!IsValidMusicId(musicId))
        {
            Console.Error.WriteLine($"musicId 非法：{musicId}");
            return 2;
        }

        var body = new JsonObject
        {
            ["musicId"] = long.Parse(musicId),
            ["freeSign"] = freeSign,
        }.ToJsonString();

        var response = await client.SendAsync(
            CheckRightPath,
            [new("musicId", musicId), new("freeSign", freeSign)],
            body,
            signed: true);

        Console.WriteLine(response.Describe());
        SaveIfRequested(save, $"checkright-{musicId}", response);

        if (response.Code != 200)
        {
            PrintBody(response, raw: false);
            return 1;
        }

        var status = response.Data?["status"]?.ToString();
        Console.WriteLine($"status：{status}（{DescribeStatus(status)}）");

        if (response.Data?["audition"] is { } audition)
        {
            Console.WriteLine("audition：");
            Console.WriteLine(audition.ToJsonString(Sanitizer.Pretty));
        }

        PrintBody(response, raw: false);
        return 0;
    }

    // ── 第 4 步：扫码登录 ───────────────────────────────────────────────────

    private const string QrCodePath = "ucenter/login/qrCode";
    private const string QrStatusPath = "ucenter/login/qrCodeStatus";
    private const string UsersLoginPath = "ucenter/users/login";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public static async Task<int> LoginAsync(ProbeClient client, int timeoutSeconds, bool save)
    {
        Console.WriteLine("扫码登录。**用小号**——这个会话接下来要做写操作探测（收藏往返、发评论）。");
        Console.WriteLine($"轮询间隔 {PollInterval.TotalSeconds:0} 秒，超时 {timeoutSeconds} 秒。");
        Console.WriteLine();

        var created = await client.SendAsync(QrCodePath, signed: true);
        Console.WriteLine($"创建二维码  {created.Describe()}");

        var key = created.Data?["qrCode"]?.ToString();
        if (created.Code != 200 || string.IsNullOrEmpty(key))
        {
            Console.Error.WriteLine("没拿到 qrCode。");
            PrintBody(created, raw: false);
            return 1;
        }

        // 扫码内容必须是完整落地页表单。放 qrCode 本身、或 login_pc?qrCode=... 都不行。
        var content = $"https://bodian-oia.kuwo.cn/bodian/download.html?pageName=login_pc&pt=3&id={key}";
        Console.WriteLine();
        TerminalQr.Draw(content);
        Console.WriteLine();
        Console.WriteLine("用波点 App 扫码。二维码内容是：");
        Console.WriteLine($"  {content}");
        Console.WriteLine();

        var confirmed = false;
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        string? lastStatus = null;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval);

            var status = await client.SendAsync(QrStatusPath, [new("qrCode", key)], signed: true);
            var value = status.Data?["status"]?.ToString();

            if (value != lastStatus)
            {
                Console.WriteLine($"  status={value}  {DescribeQrStatus(value)}");
                lastStatus = value;
            }

            switch (value)
            {
                case "3":
                    confirmed = true;
                    break;
                case "2":
                    Console.Error.WriteLine("二维码已过期，重跑 login。");
                    return 1;
                case "1":
                    break;
                default:
                    if (status.Code != 200 && status.Code != 11027)
                    {
                        Console.Error.WriteLine($"轮询返回意外业务码 {status.Code}，中止。");
                        PrintBody(status, raw: false);
                        return 1;
                    }

                    break;
            }

            if (confirmed)
            {
                break;
            }
        }

        if (!confirmed)
        {
            Console.Error.WriteLine($"等待超时（{timeoutSeconds} 秒），未扫码。");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("已扫码确认，换取会话");

        var login = await ExchangeSessionAsync(client, key);

        if (login is null)
        {
            return 1;
        }

        // id 与 bid 在这里是账号标识，必须额外抹掉——曲目响应里的 id 是 musicId，要保留，
        // 所以这个脱敏不能进全局表。
        SaveIfRequested(save, "login-users-login", login, "id", "bid");

        var data = login.Data!;
        var token = data["token"]?.ToString() ?? "";
        var info = data["userInfo"];

        // 实测（2026-09-30）：响应里没有 uid，也没有 userInfo.uid，文档 2.1 的四字段一致是错的。
        // 改成「取所有实际存在的 id 类字段，要求两两相等」——
        // 既兼容文档描述的那种形态，也不会被不存在的字段绊倒。
        var identities = new (string Name, string? Value)[]
        {
            ("id", data["id"]?.ToString()),
            ("bid", data["bid"]?.ToString()),
            ("userInfo.id", info?["id"]?.ToString()),
            ("uid", data["uid"]?.ToString()),
            ("userInfo.uid", info?["uid"]?.ToString()),
        };

        var present = identities.Where(x => !string.IsNullOrEmpty(x.Value)).ToList();

        if (string.IsNullOrEmpty(token))
        {
            Console.Error.WriteLine("响应里没有 token。");
            PrintBody(login, raw: false);
            return 1;
        }

        // 一个字段都凑不出相等关系，就没有任何证据说明这是扫码人自己的会话。
        if (present.Count < 2)
        {
            Console.Error.WriteLine("可用于校验的身份字段不足两个，无法确认身份，放弃该会话。");
            Console.Error.WriteLine($"  出现过的：{string.Join("、", present.Select(p => $"{p.Name}={p.Value}"))}");
            return 1;
        }

        // 必须两两相等。不等说明拿到的不是扫码人的会话——只能丢弃，不能存。
        if (present.Any(p => p.Value != present[0].Value))
        {
            Console.Error.WriteLine("身份错配，放弃该会话：");
            Console.Error.WriteLine("  " + string.Join("  ", present.Select(p => $"{p.Name}={p.Value}")));
            Console.Error.WriteLine("（历史坑：旧参数 authType:9 会导致这个结果，只能用 authType:10）");
            return 1;
        }

        var uid = present[0].Value!;
        var nickname = info?["nickname"]?.ToString();
        SessionStore.Save(new BodianSession(uid, token, nickname));

        Console.WriteLine($"身份一致（{present.Count} 个字段）：{string.Join(" = ", present.Select(p => $"{p.Name}:{p.Value}"))}");
        Console.WriteLine($"会话已保存  {nickname ?? "(无昵称)"} / uid={uid}");
        Console.WriteLine($"存储位置（DPAPI 加密）  {SessionStore.StoragePath}");
        Console.WriteLine();
        Console.WriteLine("现在服务端才开始校验签名。**立刻**跑一次验证，这是唯一的机会：");
        Console.WriteLine("  bodian-probe signtest <musicId>");

        return 0;
    }

    /// <summary>换取会话。11027 是「已扫码未确认」的中间态，重试而不是报错。</summary>
    private static async Task<ProbeResponse?> ExchangeSessionAsync(ProbeClient client, string key)
    {
        const int maxAttempts = 5;
        var body = new JsonObject { ["authType"] = 10, ["qrCode"] = key }.ToJsonString();

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var response = await client.SendAsync(UsersLoginPath, null, body, signed: true, method: HttpMethod.Post);
            Console.WriteLine($"  {response.Describe()}（第 {attempt}/{maxAttempts} 次）");

            if (response.Code == 200)
            {
                return response;
            }

            if (response.Code == 11027)
            {
                await Task.Delay(PollInterval);
                continue;
            }

            PrintBody(response, raw: false);
            return null;
        }

        Console.Error.WriteLine("换取会话连续返回 11027，放弃。");
        return null;
    }

    // ── 会话 ────────────────────────────────────────────────────────────────

    public static int WhoAmI()
    {
        var session = SessionStore.Load();

        if (session is null)
        {
            Console.WriteLine("未登录。");
            Console.WriteLine($"（探针检查的是 {SessionStore.StoragePath}）");
            return 1;
        }

        Console.WriteLine($"已登录：{session.Describe()}");
        Console.WriteLine($"token：{Mask(session.Token)}");
        Console.WriteLine($"存储位置：{SessionStore.StoragePath}");
        return 0;
    }

    public static int Logout()
    {
        Console.WriteLine(SessionStore.Clear() ? "已删除本地会话。" : "本来就没有本地会话。");
        return 0;
    }

    private static string Mask(string secret)
        => secret.Length <= 8 ? new string('*', secret.Length) : secret[..4] + new string('*', 8) + secret[^4..];

    // ── 第 7 步：歌词 ───────────────────────────────────────────────────────

    private const string LyricHost = "https://mlyric.kuwo.cn/mobi.s";

    public static async Task<int> LyricAsync(ProbeClient client, string musicId, bool save, int lrcx = 1)
    {
        if (!IsValidMusicId(musicId))
        {
            Console.Error.WriteLine($"musicId 非法：{musicId}");
            return 2;
        }

        // rid 填的是波点的 musicId，不是酷我 rid。
        var payload = $"type=lyric&req=2&lrcx={lrcx}&rid={musicId}&songname=&artist=&corp=kuwo&fromchannel=bodian";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        var url = $"{LyricHost}?f=bodian&q={Uri.EscapeDataString(encoded)}";

        Console.WriteLine($"payload  {payload}");
        Console.WriteLine();

        var response = await client.SendAbsoluteAsync(url);
        Console.WriteLine(response.Describe());

        var content = response.Data?["content"]?.ToString();

        if (response.Code != 200 || string.IsNullOrEmpty(content))
        {
            PrintBody(response, raw: false);
            return 1;
        }

        // 只需一次 Base64 解码。不要搬老酷我 f=web 那套 zlib + yeelion XOR。
        var text = Encoding.UTF8.GetString(Convert.FromBase64String(content));

        Console.WriteLine($"解码后 {text.Length} 字符，{text.Split('\n').Length} 行");
        Console.WriteLine();

        AnalyzeLyric(text);

        Console.WriteLine("前 12 行原文：");
        foreach (var line in text.Split('\n').Take(12))
        {
            Console.WriteLine("  " + line.TrimEnd('\r'));
        }

        if (save)
        {
            Console.WriteLine();
            Console.WriteLine($"fixture → {FixtureStore.SaveText($"lyric-{musicId}-lrcx{lrcx}", ".lrc", text)}");
        }

        return 0;
    }

    /// <summary>核对 2.6 节三处存疑：八进制系数、逐字时间是相对还是绝对、无标签时的行为。</summary>
    private static void AnalyzeLyric(string text)
    {
        var tagMatch = Regex.Match(text, @"\[kuwo:(\d+)\]");

        if (!tagMatch.Success)
        {
            Console.WriteLine("[kuwo:N] 标签：没有。按文档应默认 N=11；按参考实现应回退普通 LRC 解析。");
            Console.WriteLine();
            return;
        }

        // N 是八进制。按十进制解析会让整轨时间全废，这是最容易踩的坑。
        var n = Convert.ToInt32(tagMatch.Groups[1].Value, 8);
        var startFactor = n / 10;
        var durationFactor = n % 10;

        Console.WriteLine($"[kuwo:N] 标签：有，N={tagMatch.Groups[1].Value}（八进制）={n}（十进制）");
        Console.WriteLine($"  startFactor={startFactor}  durationFactor={durationFactor}");
        Console.WriteLine();

        if (startFactor == 0 || durationFactor == 0)
        {
            Console.WriteLine("  任一系数为 0 → 该文件无有效逐字轨，应回退行歌词。");
            Console.WriteLine();
            return;
        }

        var lineRegex = new Regex(@"^\[(\d+):(\d+)\.(\d+)\](.*)$");
        var wordRegex = new Regex(@"<(-?\d+),(-?\d+)>");

        var withWords = 0;
        var firstWordZero = 0;
        var samples = new List<string>();

        foreach (var rawLine in text.Split('\n'))
        {
            var lineMatch = lineRegex.Match(rawLine.TrimEnd('\r'));

            if (!lineMatch.Success)
            {
                continue;
            }

            var words = wordRegex.Matches(lineMatch.Groups[4].Value);

            if (words.Count == 0)
            {
                continue;
            }

            withWords++;

            var first = wordRegex.Match(lineMatch.Groups[4].Value);
            var a = double.Parse(first.Groups[1].Value, CultureInfo.InvariantCulture);
            var b = double.Parse(first.Groups[2].Value, CultureInfo.InvariantCulture);
            var start = (long)Math.Truncate(Math.Abs((a + b) / (2.0 * startFactor)));

            if (start == 0)
            {
                firstWordZero++;
            }

            if (samples.Count < 5)
            {
                samples.Add($"    {lineMatch.Groups[0].Value.Trim()}  → 首词 a={a} b={b}，算得 start={start}ms");
            }
        }

        Console.WriteLine($"含逐字的行：{withWords}");
        Console.WriteLine($"  其中首词 start == 0 的行：{firstWordZero}");

        if (withWords > 0)
        {
            Console.WriteLine(firstWordZero == withWords
                ? "  → 全部为 0。**逐字时间是相对行首的偏移**，参考实现的说法成立。"
                : firstWordZero == 0
                    ? "  → 全部不为 0。**逐字时间是绝对值**，文档的公式需要按绝对时间理解。"
                    : $"  → 只有 {firstWordZero}/{withWords} 为 0，两种情形混杂，需要逐行看。");
        }

        Console.WriteLine();
        Console.WriteLine("  样本：");
        foreach (var sample in samples)
        {
            Console.WriteLine(sample);
        }

        Console.WriteLine();
    }

    // ── 第 5 步：拿音源，直接用 ffmpeg 验 ───────────────────────────────────

    /// <summary>
    /// 档位表（实测修正版，见 bodian-api-reference.md 7.1）。
    /// **只传 br，不要传 format。** 传 <c>format=flac</c> 会被静默降级到 320k mp3
    /// （返回文件名 <c>M800….mp3</c>），不传 format 才是真无损（<c>F000000….flac</c>）。
    /// 服务端在响应里用 <c>format</c> / <c>bitrate</c> 字段明示实际给了什么，必须核对。
    /// </summary>
    private static readonly (string Name, string Br)[] Qualities =
    [
        ("standard", "128kmp3"),
        ("high", "320kmp3"),
        ("lossless", "2000kflac"),
        ("hires", "4000kflac"),
    ];

    public static async Task<int> PlayAsync(
        ProbeClient client, string musicId, string quality, int seconds, bool noPlay, string? brOverride = null)
    {
        if (!IsValidMusicId(musicId))
        {
            Console.Error.WriteLine($"musicId 非法：{musicId}");
            return 2;
        }

        // --br 直通：不经档位表，用来探任意 br 值（含加密档位）到底会拿到什么。
        var profile = brOverride is not null
            ? (Name: "custom", Br: brOverride)
            : Qualities.FirstOrDefault(q => q.Name == quality);

        if (profile.Name is null)
        {
            Console.Error.WriteLine($"未知档位：{quality}");
            Console.Error.WriteLine("可用：" + string.Join("、", Qualities.Select(q => q.Name)));
            return 2;
        }

        Console.WriteLine($"档位 {profile.Name}  br={profile.Br}");
        Console.WriteLine();

        // 先过 checkRight，再决定值不值得请求音源。
        var right = await client.SendAsync(
            CheckRightPath,
            [new KeyValuePair<string, string>("musicId", musicId), new("freeSign", "")],
            new JsonObject { ["musicId"] = long.Parse(musicId), ["freeSign"] = "" }.ToJsonString(),
            signed: true);

        var status = right.Data?["status"]?.ToString();
        Console.WriteLine($"checkRight  {right.Describe()}  status={status}（{DescribeStatus(status)}）");

        if (right.Code != 200)
        {
            PrintBody(right, raw: false);
            return 1;
        }

        if (status == "7")
        {
            Console.Error.WriteLine("没有播放权限，不再请求音源。");
            return 1;
        }

        var url = await FetchAudioUrlAsync(client, musicId, profile);

        if (url is null)
        {
            return 1;
        }

        // URL 里带签名参数，只打印主机与文件名，够判断来源了。
        Console.WriteLine($"拿到音源  {DescribeUrl(url)}");
        Console.WriteLine();

        var probed = await FfprobeAsync(url);

        if (probed != 0)
        {
            return probed;
        }

        if (noPlay)
        {
            Console.WriteLine("（--no-play，只探测不解码）");
            return 0;
        }

        Console.WriteLine($"ffplay 解码 {seconds} 秒…");
        return await RunAsync("ffplay", ["-nodisp", "-autoexit", "-loglevel", "warning", "-t", seconds.ToString(), url]);
    }

    private static async Task<string?> FetchAudioUrlAsync(
        ProbeClient client, string musicId, (string Name, string Br) profile)
    {
        var devId = client.DeviceId;

        // 刻意不带 format：带上 format=flac 反而会被降级，见 Qualities 的说明。
        var query = new List<KeyValuePair<string, string>>
        {
            new("devId", devId),
            new("musicId", musicId),
            new("br", profile.Br),
            new("freeSign", ""),
        };

        var body = new JsonObject
        {
            ["devId"] = devId,
            ["musicId"] = long.Parse(musicId),
            ["br"] = profile.Br,
            ["freeSign"] = "",
        }.ToJsonString();

        var response = await client.SendAsync(AudioUrlPath, query, body, signed: true);

        var served = response.Data?["format"]?.ToString();
        var servedBitrate = response.Data?["bitrate"]?.ToString();
        Console.WriteLine($"audioUrl    {response.Describe()}  服务端实际给了 {served} {servedBitrate}k");

        if (response.Code != 200)
        {
            PrintBody(response, raw: false);
            return null;
        }

        // 服务端会静默降级，不核对就会以为在听无损。
        if (profile.Name is "lossless" or "hires" && served != "flac")
        {
            Console.Error.WriteLine($"⚠️ 请求 {profile.Br}，服务端只给了 {served} {servedBitrate}k。");
        }

        // 优先 https，回退 http。
        var url = response.Data?["audioHttpsUrl"]?.ToString();

        if (string.IsNullOrEmpty(url))
        {
            url = response.Data?["audioUrl"]?.ToString();
        }

        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(parsed.UserInfo))
        {
            Console.Error.WriteLine("拿到的不是合法音源地址，拒绝交给播放器。");
            PrintBody(response, raw: false);
            return null;
        }

        return url;
    }

    private static string DescribeUrl(string url)
    {
        var parsed = new Uri(url);
        return $"{parsed.Host}{parsed.AbsolutePath}（query 有 {parsed.Query.Length} 字符签名参数）";
    }

    private static async Task<int> FfprobeAsync(string url)
    {
        var probe = await CaptureAsync("ffprobe",
        [
            "-v", "error",
            "-select_streams", "a:0",
            "-show_entries", "stream=codec_name,profile,sample_rate,channels,bits_per_raw_sample,bit_rate",
            "-show_entries", "format=format_name,duration,bit_rate",
            "-of", "default=noprint_wrappers=1",
            url,
        ]);

        if (probe.ExitCode != 0)
        {
            Console.Error.WriteLine($"ffprobe 失败（{probe.ExitCode}）：{probe.Stderr.Trim()}");
            return 1;
        }

        Console.WriteLine("ffprobe 实际解出的流：");
        foreach (var line in probe.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Console.WriteLine("  " + line.Trim());
        }

        Console.WriteLine();
        return 0;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> CaptureAsync(string exe, string[] args)
    {
        using var process = StartProcess(exe, args);

        if (process is null)
        {
            return (-1, "", $"{exe} 没启动起来");
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdout, await stderr);
    }

    private static async Task<int> RunAsync(string exe, string[] args)
    {
        using var process = StartProcess(exe, args);

        if (process is null)
        {
            return 1;
        }

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static Process? StartProcess(string exe, string[] args)
    {
        var startInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            return Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            Console.Error.WriteLine($"启动 {exe} 失败：{ex.Message}");
            Console.Error.WriteLine($"（ffmpeg 通常在 E:\\ffmpeg\\bin，见 docs/dev-environment.md）");
            return null;
        }
    }

    // ── 签名算法变体扫描 ────────────────────────────────────────────────────

    /// <summary>
    /// 只在 <c>ver</c> 触发强制校验时才用得上（见 2.5 节的实测）。
    /// 候选签名全部在进程内计算，不打印 query——里面有 token。
    /// </summary>
    public static async Task<int> SignSweepAsync(ProbeClient client, string musicId)
    {
        if (!IsValidMusicId(musicId))
        {
            Console.Error.WriteLine($"musicId 非法：{musicId}");
            return 2;
        }

        const string salt = "kuwotest";
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var body = new JsonObject { ["musicId"] = long.Parse(musicId), ["freeSign"] = "" }.ToJsonString();

        // 复刻 SendAsync 组 query 的顺序，保证候选签名与实际发出的请求对得上。
        var query = BodianSigner.FormUrlEncode(
        [
            new KeyValuePair<string, string>("musicId", musicId),
            new("freeSign", ""),
            new("uid", client.Uid),
            new("token", client.Token),
            new("timestamp", stamp.ToString()),
            new("sign", ""),
        ]);

        var sorted = new string(query.Where(char.IsAsciiLetterOrDigit).Order().ToArray());
        var inner = BodianSigner.Md5Hex(body + salt);
        var path = CheckRightPath;

        var candidates = new (string Name, string Sign)[]
        {
            ("salt+sorted+md5(body+salt)+path  ← 当前实现", BodianSigner.Md5Hex(salt + sorted + inner + path)),
            ("salt+sorted+md5(salt+body)+path", BodianSigner.Md5Hex(salt + sorted + BodianSigner.Md5Hex(salt + body) + path)),
            ("salt+sorted+md5(body)+path", BodianSigner.Md5Hex(salt + sorted + BodianSigner.Md5Hex(body) + path)),
            ("salt+sorted+path", BodianSigner.Md5Hex(salt + sorted + path)),
            ("salt+sorted+body+path", BodianSigner.Md5Hex(salt + sorted + body + path)),
            ("sorted+md5(body+salt)+path  ← 无前导盐", BodianSigner.Md5Hex(sorted + inner + path)),
            ("path 在前：path+salt+sorted+inner", BodianSigner.Md5Hex(path + salt + sorted + inner)),
            ("path 带斜杠：salt+sorted+inner+/path", BodianSigner.Md5Hex(salt + sorted + inner + "/" + path)),
            ("path 带前缀：salt+sorted+inner+/api/path", BodianSigner.Md5Hex(salt + sorted + inner + "/api/" + path)),
            ("当前实现的大写形式", BodianSigner.Md5Hex(salt + sorted + inner + path).ToUpperInvariant()),
        };

        Console.WriteLine("候选签名逐个试（query 与 body 已固定，只有 sign 不同）：");
        Console.WriteLine();

        var winners = new List<string>();

        foreach (var (name, sign) in candidates)
        {
            var response = await client.SendAsync(
                path,
                [new("musicId", musicId), new("freeSign", "")],
                body,
                signed: true,
                overrideSign: sign,
                fixedTimestamp: stamp);

            if (response.Code == 200)
            {
                winners.Add(name);
            }

            Console.WriteLine($"  {response.Code,6}  {name}");
            await Task.Delay(120);
        }

        Console.WriteLine();

        if (winners.Count == 0)
        {
            Console.WriteLine("十个变体全被拒。说明 ver>=3.5 的客户端用的不是这套算法，");
            Console.WriteLine("或者是同一算法但盐/输入不同——没有那个版本的客户端二进制就猜不出来。");
            return 1;
        }

        Console.WriteLine($"命中 {winners.Count} 个：" + string.Join("、", winners));
        return 0;
    }

    // ── 签名形态对照 ────────────────────────────────────────────────────────

    /// <summary>形状合法（32 位十六进制）、只有值是错的。</summary>
    private const string WrongSign = "deadbeefdeadbeefdeadbeefdeadbeef";

    /// <summary>连形状都不合法。</summary>
    private const string MalformedSign = "not-a-signature";

    private static readonly TimeSpan SignTestInterval = TimeSpan.FromMilliseconds(150);

    private sealed record SignTarget(
        string Name,
        string Path,
        IReadOnlyList<KeyValuePair<string, string>> Query,
        string Body);

    private sealed record SignWinner(SignTarget Target, PathForm Path, SignKeyForm SignKey, ProbeResponse Response);

    public static async Task<int> SignTestAsync(ProbeClient client, string musicId)
    {
        if (!IsValidMusicId(musicId))
        {
            Console.Error.WriteLine($"musicId 非法：{musicId}");
            return 2;
        }

        Console.WriteLine("每个目标接口先发两个对照请求，证明它到底看不看签名：");
        Console.WriteLine("  两个都过       → 完全不校验，对它给不出结论");
        Console.WriteLine("  只拒非法形状   → 只校验格式、不看值，仍无法确认签名算得对不对");
        Console.WriteLine("  两个都被拒     → 校验值，形态对照才有意义");
        Console.WriteLine();

        SignWinner? locked = null;

        foreach (var target in BuildSignTargets(client, musicId))
        {
            var winner = await ProbeSignTargetAsync(client, target);

            if (winner is not null && locked is null)
            {
                locked = winner;
            }

            Console.WriteLine();
        }

        if (locked is null)
        {
            Console.WriteLine("没有任何目标接口校验签名值，签名实现的正确性仍未证实。");
            Console.WriteLine("下一步：改验写接口（POST 类），或接受「读取类接口不看签名」这个事实并记账。");
            return 1;
        }

        WriteGolden(locked, verified: true,
            reason: $"{locked.Target.Name} 上无效签名被拒，且六种形态中只有本形态通过");

        return 0;
    }

    private static SignTarget[] BuildSignTargets(ProbeClient client, string musicId)
    {
        var id = long.Parse(musicId);
        var devId = client.DeviceId;

        return
        [
            new SignTarget(
                "checkRight",
                CheckRightPath,
                [new KeyValuePair<string, string>("musicId", musicId), new("freeSign", "")],
                new JsonObject { ["musicId"] = id, ["freeSign"] = "" }.ToJsonString()),

            new SignTarget(
                "audioUrl",
                AudioUrlPath,
                [
                    new KeyValuePair<string, string>("devId", devId),
                    new("musicId", musicId),
                    new("format", "mp3"),
                    new("br", "128kmp3"),
                    new("freeSign", ""),
                ],
                new JsonObject
                {
                    ["devId"] = devId,
                    ["musicId"] = id,
                    ["format"] = "mp3",
                    ["br"] = "128kmp3",
                    ["freeSign"] = "",
                }.ToJsonString()),
        ];
    }

    /// <summary>返回通过校验的那个形态；接口不校验签名值则返回 null。</summary>
    private static async Task<SignWinner?> ProbeSignTargetAsync(ProbeClient client, SignTarget target)
    {
        Console.WriteLine($"── {target.Name}   {target.Path}");

        var wrong = await SendVariantAsync(client, target, PathForm.Bare, SignKeyForm.Included, WrongSign);
        var malformed = await SendVariantAsync(client, target, PathForm.Bare, SignKeyForm.Included, MalformedSign);

        Console.WriteLine($"   对照·32 位十六进制但值是错的   {wrong.Describe()}");
        Console.WriteLine($"   对照·形状非法                 {malformed.Describe()}");

        if (wrong.Code == 200)
        {
            Console.WriteLine(malformed.Code == 200
                ? "   两个对照都通过 → 该接口完全不校验签名，对本目标无结论。"
                : "   值错也通过、只拒非法形状 → 该接口只校验格式不看值，仍无法确认签名算得对。");

            return null;
        }

        var variants = new (PathForm Path, SignKeyForm SignKey)[]
        {
            (PathForm.Bare, SignKeyForm.Included),
            (PathForm.Bare, SignKeyForm.Omitted),
            (PathForm.LeadingSlash, SignKeyForm.Included),
            (PathForm.LeadingSlash, SignKeyForm.Omitted),
            (PathForm.WithApiPrefix, SignKeyForm.Included),
            (PathForm.WithApiPrefix, SignKeyForm.Omitted),
        };

        var winners = new List<SignWinner>();

        foreach (var (path, signKey) in variants)
        {
            var response = await SendVariantAsync(client, target, path, signKey);
            var ok = response.Code == 200;

            if (ok)
            {
                winners.Add(new SignWinner(target, path, signKey, response));
            }

            Console.WriteLine($"   path={path,-14} signKey={signKey,-9} {response.Describe()}{(ok ? "   <- 通过" : "")}");
        }

        if (winners.Count == 0)
        {
            Console.WriteLine("   无效签名被拒、六种形态也全被拒：签名确实在校验，但我的实现全错。排查方向：");
            Console.WriteLine("     1. form-urlencode 的字符集是否与 URLSearchParams 一致（尤其 ~ 与空格）");
            Console.WriteLine("     2. body 的字节是否与签名时完全一致");
            Console.WriteLine("     3. 除 sign 外是否还有别的必填参数没带");
            return null;
        }

        var winner = winners[0];
        Console.WriteLine($"   锁定：path={winner.Path}，签名 query "
                          + $"{(winner.SignKey == SignKeyForm.Included ? "含" : "不含")}空 sign 参数"
                          + $"（{variants.Length - winners.Count} 种其他形态被拒）");

        return winner;
    }

    private static async Task<ProbeResponse> SendVariantAsync(
        ProbeClient client,
        SignTarget target,
        PathForm path,
        SignKeyForm signKey,
        string? overrideSign = null)
    {
        await Task.Delay(SignTestInterval);

        return await client.SendAsync(
            target.Path, target.Query, target.Body,
            signed: true, signKeyForm: signKey, pathForm: path, overrideSign: overrideSign);
    }

    private static void WriteGolden(SignWinner winner, bool verified, string reason)
    {
        var response = winner.Response;
        var sign = response.SignedQuery[(response.SignedQuery.LastIndexOf("sign=", StringComparison.Ordinal) + 5)..];

        Console.WriteLine($"种子 query：{response.SeedQuery}");
        Console.WriteLine($"body：{winner.Target.Body}");
        Console.WriteLine($"sign：{sign}");

        var golden = new JsonObject
        {
            ["note"] = "P0 signtest 候选签名用例，供 P1 做黄金用例复现",
            ["verified"] = verified,
            ["reason"] = reason,
            ["target"] = winner.Target.Name,
            ["path"] = winner.Target.Path,
            ["pathForm"] = winner.Path.ToString(),
            ["signKeyForm"] = winner.SignKey.ToString(),
            ["seedQuery"] = response.SeedQuery,
            ["body"] = winner.Target.Body,
            ["sign"] = sign,
        };

        Console.WriteLine($"fixture → {FixtureStore.Save("sign-golden", golden.ToJsonString(Sanitizer.Pretty))}");
    }

    // ── 任意接口 ────────────────────────────────────────────────────────────

    public static async Task<int> CallAsync(
        ProbeClient client,
        string path,
        IReadOnlyList<KeyValuePair<string, string>> query,
        string? body,
        bool signed,
        HttpMethod method,
        bool save,
        string? name,
        string? forceSign = null,
        bool mobileSign = false)
    {
        var response = await client.SendAsync(path, query, body, signed, method: method, overrideSign: forceSign, mobileSign: mobileSign);
        Console.WriteLine(response.Describe());
        SaveIfRequested(save, name ?? "call-" + path.Replace('/', '-'), response);

        if (response.Code != 200)
        {
            PrintBody(response, raw: false);
            return 1;
        }

        PrintBody(response, raw: false);
        return 0;
    }

    // ── 歌单封面：multipart 上传 ─────────────────────────────────────────────

    /// <summary>
    /// 上传歌单封面。端点与字段名（<c>file</c>）来自反汇编，见
    /// <c>reverse/findings/11-share-playlist-crud.md</c>。
    /// </summary>
    /// <remarks>
    /// <b>这是写操作，会上传真实文件</b>。配合编辑接口的实测序列时，只对刚建的测试歌单用；
    /// 传已有歌单的 id 会把它自己的封面换掉。
    /// </remarks>
    public static async Task<int> UploadPicAsync(
        ProbeClient client, string playlistId, string filePath, bool signed, bool signBodyBytes, bool save)
    {
        if (!long.TryParse(playlistId, out var id) || id <= 0)
        {
            Console.Error.WriteLine($"playlistId 非法：{playlistId}（要求正整数）");
            return 2;
        }

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"文件不存在：{filePath}");
            return 2;
        }

        var bytes = await File.ReadAllBytesAsync(filePath);
        var contentType = ImageContentType(filePath);
        var path = $"service/playlist/uploadPic/{id}";

        Console.WriteLine($"上传 {Path.GetFileName(filePath)}（{bytes.Length} 字节，{contentType}）→ 歌单 {id}");
        Console.WriteLine();

        var response = await client.SendMultipartAsync(
            path,
            query: null,
            fieldName: "file",
            fileName: Path.GetFileName(filePath),
            contentType: contentType,
            content: bytes,
            signed: signed,
            method: HttpMethod.Post,
            signBodyBytes: signBodyBytes);

        Console.WriteLine(response.Describe());
        SaveIfRequested(save, $"playlist-uploadpic-{id}", response);

        if (response.Code != 200)
        {
            PrintBody(response, raw: false);
            return 1;
        }

        PrintBody(response, raw: false);

        // 回执里封面 URL 的键名未实测，把像 URL 的候选键都列出来。
        Console.WriteLine();
        foreach (var key in (string[])["imgUrl", "imgurl", "pic", "url", "cover", "coverUrl"])
        {
            if (response.Data?[key]?.ToString() is { Length: > 0 } value)
            {
                Console.WriteLine($"封面 URL（data.{key}）：{value}");
            }
        }

        return 0;
    }

    private static string ImageContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "image/jpeg",
    };

    // ── 输出 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 无论业务码是什么都存——错误响应（20012 已下线、20018 无权限）正是最该留档的样本。
    /// </summary>
    private static void SaveIfRequested(bool save, string name, ProbeResponse response, params string[] extraMaskedKeys)
    {
        if (!save)
        {
            return;
        }

        Console.WriteLine($"fixture → {FixtureStore.Save(name, response.RawBody, extraMaskedKeys)}");
    }

    private static void PrintTrack(JsonNode? data)
    {
        if (data is null)
        {
            Console.WriteLine("data 为空。");
            return;
        }

        Console.WriteLine($"id：{data["id"]}");
        Console.WriteLine($"name：{data["name"] ?? data["songName"]}");
        Console.WriteLine($"artist：{data["artist"]}（artistId={data["artistId"]}）");
        Console.WriteLine($"album：{data["album"]}（albumId={data["albumId"]}）");
        Console.WriteLine($"duration：{data["duration"]} ms");

        if (data["audios"] is JsonArray audios)
        {
            Console.WriteLine($"audios（{audios.Count}）：");
            foreach (var audio in audios)
            {
                Console.WriteLine($"  level={audio?["level"],-6} format={audio?["format"],-6} "
                                  + $"bitrate={audio?["bitrate"],-6} size={audio?["size"]}");
            }
        }

        if (data["payInfo"] is JsonObject payInfo)
        {
            Console.WriteLine("payInfo：");
            foreach (var (key, value) in payInfo)
            {
                if (key == "paytagindex")
                {
                    continue;
                }

                Console.WriteLine($"  {key} = {value?.ToJsonString()}");
            }

            if (payInfo["paytagindex"] is JsonObject tags)
            {
                Console.WriteLine("  paytagindex = " + string.Join(", ", tags.Select(t => $"{t.Key}:{t.Value}")));
            }
        }
    }

    private static void PrintBody(ProbeResponse response, bool raw)
    {
        var text = raw ? response.RawBody : Sanitizer.ForDisplay(response.RawBody);
        Console.WriteLine();
        Console.WriteLine(text);
    }

    private static string DescribeStatus(string? status) => status switch
    {
        "3" => "仅试听，只能播放 audition 给的区间",
        "7" => "缺少播放权限",
        null => "响应里没有 status 字段",
        _ => "有完整播放权限",
    };

    private static string DescribeQrStatus(string? status) => status switch
    {
        "1" => "等待扫码",
        "2" => "已过期",
        "3" => "已扫码确认",
        null => "响应里没有 status 字段",
        _ => "未知状态",
    };

    private static bool IsValidMusicId(string musicId)
        => musicId.Length is >= 1 and <= 20
           && musicId.All(char.IsAsciiDigit)
           && musicId.Any(c => c != '0');
}
