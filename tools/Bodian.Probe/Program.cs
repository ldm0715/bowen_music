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
var post = false;
var save = false;
var verbose = false;

for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];

    switch (arg)
    {
        case "-h" or "--help":
            Usage.Print();
            return 0;
        case "--post":
            post = true;
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

if (command == "call")
{
    if (positional.Count < 2)
    {
        Console.Error.WriteLine("call 缺少 <path> 参数。");
        Usage.Print();
        return 2;
    }

    using var callClient = new ProbeClient(proxy, verbose);
    return await Commands.CallAsync(callClient, positional[1], queryPairs, body, signed, post, save, fixtureName);
}

if (command is "info" or "checkright" or "signtest")
{
    if (positional.Count < 2)
    {
        Console.Error.WriteLine($"{command} 缺少 <musicId> 参数。");
        Usage.Print();
        return 2;
    }

    var musicId = positional[1];
    var freeSign = queryPairs.FirstOrDefault(p => p.Key == "freeSign").Value ?? "";

    using var client = new ProbeClient(proxy, verbose);

    if (command == "info")
    {
        return await Commands.InfoAsync(client, musicId, save);
    }

    return command == "checkright"
        ? await Commands.CheckRightAsync(client, musicId, freeSign, save)
        : await Commands.SignTestAsync(client, musicId);
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
