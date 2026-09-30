using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bodian.Probe;

internal static class Commands
{
    private const string InfoPath = "service/music/info";
    private const string CheckRightPath = "play/music/v2/checkRight";

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

    // ── 签名形态对照 ────────────────────────────────────────────────────────

    public static async Task<int> SignTestAsync(ProbeClient client, string musicId)
    {
        if (!IsValidMusicId(musicId))
        {
            Console.Error.WriteLine($"musicId 非法：{musicId}");
            return 2;
        }

        var pairs = new[] { new KeyValuePair<string, string>("musicId", musicId) };
        var body = new JsonObject { ["musicId"] = long.Parse(musicId), ["freeSign"] = "" }.ToJsonString();

        var variants = new (PathForm Path, SignKeyForm SignKey)[]
        {
            (PathForm.Bare, SignKeyForm.Included),
            (PathForm.Bare, SignKeyForm.Omitted),
            (PathForm.LeadingSlash, SignKeyForm.Included),
            (PathForm.LeadingSlash, SignKeyForm.Omitted),
            (PathForm.WithApiPrefix, SignKeyForm.Included),
            (PathForm.WithApiPrefix, SignKeyForm.Omitted),
        };

        Console.WriteLine($"用 checkRight（musicId={musicId}）逐一试签名形态。");
        Console.WriteLine("判据：只有一部分形态能过，才说明服务端在校验签名，过的那些才是正确形态；");
        Console.WriteLine("全都过 = 服务端没校验，本命令无法给出结论。");
        Console.WriteLine();

        var results = new List<(PathForm Path, SignKeyForm SignKey, ProbeResponse Response, bool Ok)>();
        var anonymous = client.Uid == "-1";

        foreach (var (path, signKey) in variants)
        {
            var response = await client.SendAsync(
                CheckRightPath, pairs, body, signed: true, signKeyForm: signKey, pathForm: path);

            var ok = response.Code == 200;
            results.Add((path, signKey, response, ok));

            Console.WriteLine($"path={path,-14} signKey={signKey,-9} {response.Describe()}{(ok ? "   <- 通过" : "")}");
        }

        Console.WriteLine();

        var winners = results.Where(p => p.Ok).ToList();

        if (winners.Count == 0)
        {
            Console.WriteLine("六种形态全部失败。排查方向：");
            Console.WriteLine("  1. 请求头是否被服务端接受（缺头返回 402，不是签名错）");
            Console.WriteLine("  2. form-urlencode 的字符集是否与 URLSearchParams 一致（尤其 ~ 与空格）");
            Console.WriteLine("  3. body 的字节是否与签名时完全一致");
            return 1;
        }

        var verified = winners.Count < variants.Length;

        if (!verified)
        {
            Console.WriteLine($"六种形态全部通过 —— checkRight 在当前会话（uid={client.Uid}）下不校验签名，");
            Console.WriteLine("本命令无法锁定形态。签名是否正确要等登录后再验（token 非空时服务端才可能启用校验）。");
            Console.WriteLine();
            WriteGolden(winners[0], body, verified: false, reason: anonymous
                ? "匿名会话下 checkRight 不校验签名，此用例未经证实"
                : "当前会话下 checkRight 不校验签名，此用例未经证实");

            return 0;
        }

        var winner = winners[0];
        Console.WriteLine($"锁定形态：path={winner.Path}，签名 query "
                          + $"{(winner.SignKey == SignKeyForm.Included ? "含" : "不含")}空 sign 参数。");
        Console.WriteLine($"{variants.Length - winners.Count} 种其他形态被拒，说明服务端确实在校验。");

        WriteGolden(winner, body, verified: true, reason: "其余形态被服务端拒绝，本形态通过");
        return 0;
    }

    private static void WriteGolden(
        (PathForm Path, SignKeyForm SignKey, ProbeResponse Response, bool Ok) winner,
        string body,
        bool verified,
        string reason)
    {
        var sign = winner.Response.SignedQuery[(winner.Response.SignedQuery.LastIndexOf("sign=", StringComparison.Ordinal) + 5)..];

        Console.WriteLine($"种子 query：{winner.Response.SeedQuery}");
        Console.WriteLine($"body：{body}");
        Console.WriteLine($"sign：{sign}");

        var golden = new JsonObject
        {
            ["note"] = "P0 signtest 候选签名用例，供 P1 做黄金用例复现",
            ["verified"] = verified,
            ["reason"] = reason,
            ["path"] = CheckRightPath,
            ["pathForm"] = winner.Path.ToString(),
            ["signKeyForm"] = winner.SignKey.ToString(),
            ["seedQuery"] = winner.Response.SeedQuery,
            ["body"] = body,
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
        bool post,
        bool save,
        string? name)
    {
        var response = await client.SendAsync(path, query, body, signed, post: post);
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

    // ── 输出 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 无论业务码是什么都存——错误响应（20012 已下线、20018 无权限）正是最该留档的样本。
    /// </summary>
    private static void SaveIfRequested(bool save, string name, ProbeResponse response)
    {
        if (!save)
        {
            return;
        }

        Console.WriteLine($"fixture → {FixtureStore.Save(name, response.RawBody)}");
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

    private static bool IsValidMusicId(string musicId)
        => musicId.Length is >= 1 and <= 20
           && musicId.All(char.IsAsciiDigit)
           && musicId.Any(c => c != '0');
}
