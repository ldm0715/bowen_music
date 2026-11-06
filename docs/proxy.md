# 网络与代理

**2026-10-14 定稿并实现。** 目标是让应用**不受外部系统代理影响**，并把「更新检查走哪条路」交给用户。

相关：[`update.md`](update.md)（更新通道本身）、[`settings.md`](settings.md)（设置页与新增的「网络」分类）、[`transport.md`](transport.md)（传输层）。

---

## 1. 症状与根因

**症状：外面开着代理，应用的请求也跟着绕。**

根因在 `BodianHttpTransport.CreateHandler`（`src/Bowen.Core/Api/BodianHttpTransport.cs`）：

```csharp
if (options.Proxy is { } proxy)      // 生产环境谁也没填，是 null
{
    handler.Proxy = new WebProxy(proxy);
    handler.UseProxy = true;
}
```

`Proxy` 为 null 时这两个属性一个都不设，而 `SocketsHttpHandler.UseProxy` 的**默认值是 `true`**，
于是落到 `HttpClient.DefaultProxy` —— 也就是**系统代理**。

`BodianTransportOptions.Proxy` 这个口子是留给「本机开发」的，生产路径从来没填过它，
所以它今天等价于「跟随系统代理」，只是没人明说。

**既有的测试没挡住这个**：`TransportTests.CreateHandler_ConfiguresDecompressionAndPooling`
断言的是 `Assert.Null(handler.Proxy)` —— 在跟随系统代理时它**恰好也成立**（Proxy 确实是 null，
走的是 UseProxy 那条默认路径）。漏掉的正是 `UseProxy` 本身。这次一并补上断言。

## 2. 各条网络路径的实际归属

先分清谁受谁影响，不然「隔离」会做错地方。下表是**改动前**的归属：

| 路径 | 网络栈 | 受系统代理影响 | 受 `http_proxy` 环境变量影响 |
| --- | --- | --- | --- |
| API JSON（登录、歌单、搜索…） | .NET（我们那个 handler） | **是** | **是**（.NET 也读环境变量） |
| 封面下载 | 同上（`CoverDiskCache` 复用同一 handler） | **是** | **是** |
| 更新检查 / 安装包下载 | 同上 | **是** | **是** |
| 音频流 / MV | libmpv + ffmpeg | **否** | **是** |

改动后：API 与封面固定直连；更新检查与安装包下载走可配置的 `UpdateProxy`（默认直连）；
音频 / MV 仍只受 `http_proxy` 这类环境变量影响。

关键的一条：**libmpv 那条路与 .NET 无关**。ffmpeg 的 http 协议不读 Windows 的 WinINET 系统设置，
只认 `http_proxy` / `https_proxy` 环境变量。所以「开了 Clash 的系统代理，音频就跟着绕」这件事
**不会发生**；会被影响的只有 .NET 那三条。

## 3. 决策

| | 走哪条路 | 可否配置 |
| --- | --- | --- |
| **常规请求**（API、封面） | **固定直连**，永不受系统代理影响 | 否 |
| **更新检查 / 安装包下载** | 按设置：不使用代理 / 跟随系统 / 自定义 | **是** |
| **音频流 / MV** | libmpv 自己的事，不动 | 否 |

**为什么业务请求做成固定直连而不是也开一个开关。** 业务全在国内（`bd-api.kuwo.cn` 与它的
CDN），直连永远是对的选择；给一个「业务也走代理」的开关，唯一的用途是把国内流量绕出去，
那不是配置项，那是故障。少一个开关就少一类「怎么突然变慢了」的问题。

**为什么更新那条必须可配。** `api.github.com` 在国内直连基本超时。默认直连（真隔离），
但得给用户一条「我只让更新走代理」的路 —— 这是本次改动的实际价值所在。

## 4. 怎么实现

### 4.1 业务：显式关掉代理

`CreateHandler` 改成**永远显式设 `UseProxy`**，不再依赖默认值：

```csharp
UseProxy = options.Proxy is not null,
Proxy = options.Proxy is { } proxy ? new WebProxy(proxy) : null,
```

`BodianTransportOptions.Proxy` 保留，但语义收窄为**测试与本地开发的覆盖口，生产恒为 null**
（注释要改，现在那条写的是「本机开发在 7890 端口时传」——改完之后本地也不需要它了）。

### 4.2 更新通道：一个会读实时设置的 `IWebProxy`

新增 `Bowen.Core/Services/UpdateProxy.cs`：

```csharp
public sealed class UpdateProxy : IWebProxy
{
    private ProxySettings _settings;   // 当前值
    private Uri? _custom;              // 自定义模式下解析好的地址，不合法时是 null

    public ProxySettings Current { get; }
    public void Apply(ProxySettings settings);

    public Uri? GetProxy(Uri destination) => _settings.Mode switch
    {
        ProxyMode.Direct => null,                        // 直连
        ProxyMode.System => HttpClient.DefaultProxy.GetProxy(destination),
        ProxyMode.Custom => _custom,                     // 地址不合法时是 null → 直连
        _ => null,
    };

    public bool IsBypassed(Uri host) => GetProxy(host) is null;
}
```

它同时是**当前设置的持有者**（界面读 `Current`）与**handler 要的那个 `IWebProxy`**（每个请求问一次），
所以设置改完不需要重建任何东西。这两个职责放在一个类里是刻意的：它们是同一件事的两面。

配套一个 `Handler` 属性（第一次取到时建，之后是同一个），把 handler 的配置与代理逻辑放在一起，
与 `BodianHttpTransport.CreateHandler` 对称：

```csharp
new SocketsHttpHandler
{
    AutomaticDecompression = DecompressionMethods.All,
    UseProxy = true,      // 必须显式设：不设会落回系统代理；具体走不走由本类的 GetProxy 决定
    Proxy = this,
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
}
```

**`GitHubReleaseUpdateService` 的构造函数一个字不改。** 它照旧收一个 `HttpMessageHandler`，
换的只是 `App.xaml.cs` 里怎么造那个 handler —— 这样既有的 8 处测试（注入桩 handler）全都不用动。

那个 handler 现在**由 `UpdateProxy` 自己持有并释放**（`UpdateProxy` 因此实现了 `IDisposable`）：
它原来是容器里注册的共享单例、退出时由容器统一 Dispose，改成现建之后归属就得跟着挪，
否则会漏掉一个没人释放的 handler。

「跟随系统」用 `HttpClient.DefaultProxy`，即**改动前那条路**。它是进程启动时取的快照，
运行中改系统代理后是否需要重启应用，由 .NET 的实现决定，界面不做保证（见 §6）。

### 4.3 设置模型

```csharp
public enum ProxyMode { Direct, System, Custom }

public sealed record ProxySettings(ProxyMode Mode = ProxyMode.Direct, string Address = "")
{
    public static ProxySettings Default { get; } = new();   // 默认直连
    public bool TryGetCustomUri(out Uri? uri, out string error);
}
```

地址校验（`TryGetCustomUri`）的规则：

- 去空白后不能为空；
- 必须是绝对 URI，scheme 只认 `http` / `https` / `socks4` / `socks5`；
- 必须有主机名与端口。

**地址里可以带用户名密码**（`http://user:pass@127.0.0.1:7890`），解析成 `NetworkCredential`
交给 `IWebProxy.Credentials`。不做单独的凭据界面：本地代理（Clash 这类）不需要，
而企业代理那种需求给一个 URL 足够表达，单开用户名/密码两个框只是把同一件事说两遍。

存 `config.json` 的 `proxy` section（旧版是 `proxy.json`），明文 —— 与其它偏好一样，不含账号标识。
**它确实是凭据**（如果用户在 URL 里写了密码），这一点在文件里写清楚：
凭据类的东西本来是走 DPAPI 的（`session.dat` / `remembered-accounts.dat`），
这里从简的理由是「用户自己填的代理地址，且本地代理占绝大多数」。
**如果以后要收企业代理的账号密码，这条得改成 DPAPI。**

### 4.4 界面：新增「网络」分类

分类栏从六个变七个：**外观 / 播放 / 歌词 / 网络 / 快捷键 / 存储 / 关于**。

> ★ 插入在中间会让 `SettingsViewModel` 里那六个 `IsXxxSelected => SelectedCategoryIndex == n`
> 的下标整体后移，改的时候六个都要跟着动，漏一个就是「点甲显示乙」。
> 页面里的分区顺序（`SettingsPage.xaml` 那串 `StackPanel`）也要同步。

一页三行 + 一行状态：

| 行 | 形态 | 内容 |
| --- | --- | --- |
| 常规请求 | 只读文字「固定直连」 | 说明「登录、歌单、封面这些请求固定直连，不受系统代理影响」 |
| 检查更新使用的代理 | 下拉：不使用代理 / 跟随系统 / 自定义 | 说明更新与下载走这条路 |
| 代理地址 | 输入框，`IsRowEnabled` 绑「模式 == 自定义」 | 占位说明给例子；**失焦才写盘**（`UpdateSourceTrigger` 不写，默认就是 LostFocus），否则每敲一个字写一次文件 |
| 状态行 | `BowenPageStatus` | 地址有效时回显实际生效的地址，无效时说清哪不对 |

图标新增一颗 `IconGlobe`（`globe_24_regular`，Fluent System Icons，MIT，
完整 24×24 坐标 + `F1` 前缀）。**新增图标只改 `Themes/Icons.xaml` 一处**，见 `icons.md` 的规约。

## 5. 明确的边界

- **TUN 模式抓不住。** 代理软件开 TUN 时是在 IP 层拦包，`SocketsHttpHandler` 的
  `UseProxy=false` 拦不住它。这不在应用能管的范围里，写清楚比假装能解决强。
- **`http_proxy` 环境变量不处理。** 设了这个变量的机器上，.NET 与 libmpv 都会读它。
  不显式屏蔽的理由：mpv 那侧要靠 `--http-proxy` 选项覆盖，而它的「空值算不算覆盖」这件事
  **没有实测过**，盲改一个选项有可能把音频整个弄哑 —— 收益（少数用户）与风险不成比例。
  真碰到再加，那时先写一个 mpv 探针把语义测清楚。
- **不做 PAC / 按主机分流。** 「国内直连、国外走代理」这件事由代理软件自己的规则做
  （Clash 的 rule 模式就是干这个的），应用再做一层只会和它打架。
- **不代填认证界面的交互。** 见 §4.3：地址里带 userinfo 就够了。

## 6. 核查与尚未验证的部分

### 已经落地并自动验过的（2026-10-14，全量 1674 项测试通过，本轮新增 36 项）

- **业务 handler 现在显式 `UseProxy = false`。** 原来那条断言（`Assert.Null(handler.Proxy)`）
  在跟随系统代理时**也成立**，所以挡不住这个问题；新加的一条直接断言 `UseProxy` 本身。
- 地址校验的每一条分支（含「必须写端口」那个坑：`Uri` 会把默认端口规范化掉，只能看原串）。
- 三种模式的 `GetProxy` / `IsBypassed` 判定；自定义地址不合法时退回直连；
  userinfo 从端点里剥干净、单独走 `Credentials`（含 `%40` 这类转义）。
- 跟随系统那一路把系统代理的凭据一起转出来。
- 存的是枚举名而不是数字，且认不出来的值（手改文件写成 `7`）收口成直连。

### 还没验的

- **改设置后不重启是否立刻生效。** 设计上 `SocketsHttpHandler` 每个请求都会问一次
  `IWebProxy.GetProxy`，所以应该是「下一个请求就按新设置走」。**这一条要实测**：
  改完模式直接点「检查更新」，看它是不是按新的走。若实际要重启才生效，
  退路是在 `Apply` 之后重建 handler（`GitHubReleaseUpdateService` 的构造函数会成为改造点）。
- 带用户名密码的代理地址能否正常认证（手上没有需要认证的代理可测）。
- 界面整体没跑过（按项目规矩前端验证交人工）。需要人工过的：
  - 分类栏从六项变七项，**点每一项都显示对应的那一页**（插入「网络」时后面四项的编号整体后移过）；
  - 「网络」那一页三行的排布、下拉切换后地址行的置灰与解除、地址失焦才写盘；
  - 把模式改成「自定义」并填 `http://127.0.0.1:7890`，点「检查更新」能查到新版本
    （**这是本次改动唯一真正的验收点**：以前它靠系统代理撞运气，现在靠自己那条路）；
  - 反过来，把模式设成「不使用代理」、外面开着系统代理，业务请求（登录、歌单、封面）
    应当照常工作 —— 这一条证明隔离真的生效了。
