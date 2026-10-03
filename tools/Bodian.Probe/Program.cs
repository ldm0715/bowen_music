using System.Text;
using Bodian.Probe;

try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // 输出被重定向时可能设不上，不影响逻辑。
}

var positional = new List<string>();
var queryPairs = new List<KeyValuePair<string, string>>();
string? body = null;
string? proxy = Environment.GetEnvironmentVariable("BODIAN_PROXY");
string? fixtureName = null;
var signed = false;
HttpMethod? method = null;
var save = false;
var verbose = false;
var timeoutSeconds = 300;
var quality = "lossless";
var seconds = 20;
var noPlay = false;
string? brOverride = null;
var lrcx = 1;
string? verOverride = null;
string? forceSign = null;
var mobileSign = false;
string? platOverride = null;
var anonymous = false;

for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];

    switch (arg)
    {
        case "-h" or "--help":
            Usage.Print();
            return 0;
        case "--post":
            method = HttpMethod.Post;
            break;
        case "--delete":
            method = HttpMethod.Delete;
            break;
        case "--plat":
            platOverride = NextValue(args, ref i);
            break;

        case "--mobile-sign":
            mobileSign = true;
            break;

        case "--signed":
            signed = true;
            break;
        case "--save":
            save = true;
            break;
        case "--verbose":
            verbose = true;
            break;
        case "--quality":
            quality = NextValue(args, ref i) ?? quality;
            break;
        case "--seconds":
            if (int.TryParse(NextValue(args, ref i), out var playSeconds) && playSeconds > 0)
            {
                seconds = playSeconds;
            }

            break;
        case "--br":
            brOverride = NextValue(args, ref i);
            break;
        case "--anonymous":
            anonymous = true;
            break;
        case "--force-sign":
            forceSign = NextValue(args, ref i);
            break;
        case "--ver":
            verOverride = NextValue(args, ref i);
            break;
        case "--lrcx":
            if (int.TryParse(NextValue(args, ref i), out var lrcxValue))
            {
                lrcx = lrcxValue;
            }

            break;
        case "--no-play":
            noPlay = true;
            break;
        case "--timeout":
            if (int.TryParse(NextValue(args, ref i), out var timeoutValue) && timeoutValue > 0)
            {
                timeoutSeconds = timeoutValue;
            }

            break;
        case "--body":
            body = NextValue(args, ref i);
            break;
        case "--proxy":
            proxy = NextValue(args, ref i);
            break;
        case "--name":
            fixtureName = NextValue(args, ref i);
            break;
        default:
            if (!arg.StartsWith("--", StringComparison.Ordinal) && arg.Contains('='))
            {
                var split = arg.IndexOf('=');
                queryPairs.Add(new KeyValuePair<string, string>(arg[..split], arg[(split + 1)..]));
            }
            else
            {
                positional.Add(arg);
            }

            break;
    }
}

if (positional.Count == 0)
{
    Usage.Print();
    return args.Length == 0 ? 0 : 2;
}

var command = positional[0];

if (command == "devid")
{
    return Commands.Devid();
}

if (command == "logout")
{
    return Commands.Logout();
}

if (command == "login")
{
    // 登录刻意不加载已有会话：要拿的是新会话，带上旧 token 反而可能换回旧账号。
    using var loginClient = new ProbeClient(proxy, verbose);
    return await Commands.LoginAsync(loginClient, timeoutSeconds, save);
}

// 除上面三条外，其余命令一律带会话跑——signtest 的意义就在于「登录后签名是否被校验」。
using var client = new ProbeClient(proxy, verbose)
{
    Plat = platOverride ?? "win",
};

if (!anonymous && SessionStore.Load() is { } session)
{
    client.Uid = session.Uid;
    client.Token = session.Token;

    if (verbose)
    {
        Console.Error.WriteLine($"> 使用已保存会话：{session.Describe()}");
    }
}

if (verOverride is not null)
{
    client.Version = verOverride;
}

if (command == "whoami")
{
    return Commands.WhoAmI();
}

if (command == "call")
{
    if (positional.Count < 2)
    {
        Console.Error.WriteLine("call 缺少 <path> 参数。");
        Usage.Print();
        return 2;
    }

    return await Commands.CallAsync(
        client, positional[1], queryPairs, body, signed, method ?? HttpMethod.Get, save, fixtureName, forceSign, mobileSign);
}

if (command is "info" or "checkright" or "signtest" or "play" or "lyric" or "sigsweep")
{
    if (positional.Count < 2)
    {
        Console.Error.WriteLine($"{command} 缺少 <musicId> 参数。");
        Usage.Print();
        return 2;
    }

    var musicId = positional[1];
    var freeSign = queryPairs.FirstOrDefault(p => p.Key == "freeSign").Value ?? "";

    switch (command)
    {
        case "info":
            return await Commands.InfoAsync(client, musicId, save);
        case "checkright":
            return await Commands.CheckRightAsync(client, musicId, freeSign, save);
        case "play":
            return await Commands.PlayAsync(client, musicId, quality, seconds, noPlay, brOverride);
        case "lyric":
            return await Commands.LyricAsync(client, musicId, save, lrcx);
        case "sigsweep":
            return await Commands.SignSweepAsync(client, musicId);
        default:
            return await Commands.SignTestAsync(client, musicId);
    }
}

Console.Error.WriteLine($"未知命令：{command}");
Usage.Print();
return 2;

static string? NextValue(string[] args, ref int index)
{
    if (index + 1 >= args.Length)
    {
        Console.Error.WriteLine($"选项 {args[index]} 缺少值。");
        return null;
    }

    return args[++index];
}
