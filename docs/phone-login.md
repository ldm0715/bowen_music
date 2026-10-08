# 手机号登录

> **协议结论先读 [`reverse/findings/16-phone-login.md`](../reverse/findings/16-phone-login.md)。**
> 本文只讲实现，不重复协议细节。

## 为什么加

原来只有扫码一条路，要求手机在手边且装了波点 App。在 PC 前想快速登录时那是多余的步骤；
官方 PC 客户端本身也有手机号登录，所以补齐它属于「对齐官方能力」。

## 两条路径的关系

```
LoginPage（一个居中 ContentDialog，顶部 PillTabBar 两个页签）
├─ 扫码    CreateChallengeAsync → 轮询 → CompleteAsync ─┐
└─ 手机号  SendSmsCodeAsync     → LoginByPhoneAsync   ─┤
                                                       ↓
                                     BodianLogin.Adopt（同一条落地路径）
                                     → 身份校验 → 先落盘 → 再改会话 → AccountChanged
```

**两条路唯一的出口是 `LoginViewModel.DescribeOutcome`**，成功时也只有它触发 `LoggedIn`。
`LoginPage.xaml.cs` 因此完全不需要知道用户是怎么登录的 —— 它只订阅 `LoggedIn`。

`Adopt` 是复用的核心：它做了身份一致性校验（`SessionIdentity`）、**先落盘再改内存会话**、
会员档位判读、`AccountChanged` 通知。手机号路径一行都不用重写这些。

## 关键实现决定

### 发码不复用 `LoginOutcome`

`LoginOutcome.Success(Uid, Nickname)` 的语义是「会话已写入并落盘」。发验证码根本不碰会话，
借用它会让调用方以为已经登录了。所以另开
[`SmsSendOutcome`](../src/Bodian.Core/Models/Login/SmsSendOutcome.cs)：`Sent` / `Failed`。

### 手机号格式校验在 Core，不在 ViewModel

`MobileNumber`（[`Services/MobileNumber.cs`](../src/Bodian.Core/Services/MobileNumber.cs)）
是纯函数，和 `SessionIdentity` 同一种东西，放在 Core 里才能离屏单测。
界面只用它开关按钮，不写第二套判断。

**这不是替服务端做参数校验** —— 服务端仍会自己判一遍并把结论放进业务码。
本地判一次只是让明显不合法的输入不打网络、不占风控计数。

### 预期内的业务码走 `AcceptedCodes`

`BodianHttpTransport` 的约定是：**非 200 且不在 `AcceptedCodes` 里的一律抛
`BodianApiException`**（`MvUnavailable`、`TrackOffline` 都是这么做的）。所以：

- 发码接受 `11003`（短信发送失败 → `SmsSendOutcome.Failed`）
- 换会话接受 `11004`（验证码错误 → `LoginOutcome.Failed`）
- **不接受 `11027`** —— 那是扫码轮询专有的「已扫未确认」。手机号是一次性提交，
  撞上它就该立刻结束，重试只会白打几次风控。

### 倒计时与轮询互斥

两个页签的内容**都留在可视树上**，只切 `Visibility`（沿用 `ArtistDetailPage` 的做法），
所以两条路径的后台动作必须显式互斥：

- 切到手机号 → `StopPolling()`。否则用户在输手机号，后台还在每 2 秒打一次 `qrCodeStatus`，最长 5 分钟。
- 切回扫码 → 重新 `StartAsync()` 取一张新码。

倒计时用**独立的 `CancellationTokenSource`**，绝不能与扫码轮询共用 —— 共用会让切页签互相掐死。

### 状态文字两条路径各存一份

`QrStatusText` 与 `PhoneStatusText` 分开，**不共用**，各写在各面板内部。

一开始是共用一条放在两个面板下面，结果就是切到手机号登录后，下面还写着
「用波点 App 扫描二维码」—— 两个面板同时留在可视树上，扫码那一路的文字当然还在。
更麻烦的是扫码的轮询在切走后仍可能把文字写回来，覆盖掉手机号那一边的提示。

所以 `DescribeOutcome` **返回文字而不是直接写属性**：它只负责分派与副作用（成功时触发
`LoggedIn`），写给哪一条由调用方决定。

### 线程亲和性

`LoginViewModel` 里所有 `await` 都不加 `ConfigureAwait(false)`。这些属性被 `x:Bind` 绑着，
续体跑回线程池会抛 `0x8001010E`（`AccountViewModel` 记过这个坑）。
倒计时也一样：`await Task.Delay` 的续体自然落回 UI 线程，不需要额外的调度器。

### 手机号是 PII

- 不写日志。`BodianHttpTransport` 只记 `SafeUrl`（scheme + host + path），
  手机号在 query 里，**不进日志** —— 但调试时注意 `tools/Bodian.Probe --verbose` 会把完整 URL 打到 stderr。
- 不落盘、不进 `BodianCredential`。凭据格式是对外契约，加字段会影响从磁盘恢复。
- 不进 fixture。`tools/Bodian.Probe/Sanitizer.cs` 的脱敏键表里有 `mobile` / `phone`。

## 已知限制

| 项 | 说明 |
| --- | --- |
| 服务端限流码未知 | 没把请求打到触发限流，所以界面只能原样显示业务码。60 秒客户端冷却 + 不自动重试是当前的兜底 |
| `11003` 语义不唯一 | 「号码无效」与「字段名不对」同码，见 findings/16 第 5 节 |
| 探针与客户端共用 `session.dat` | 探针的会话只存 `uid/token/nickname`，写一次会丢掉头像与会员档位。手动排查后如果发现这两项没了，先查这里 |

## 前端验收清单

1. 弹窗**没有**「登录」大标题，顶部直接是两个等宽、居中的页签，药丸高亮是滑动的（不是瞬间跳）。
2. 切到手机号页签后，等几秒 —— 抓包或代理里**不应再出现** `ucenter/login/qrCodeStatus` 请求。
3. 切到手机号页签后，状态行**不应**残留「用波点 App 扫描二维码」这类扫码文案。
4. 输错位数（如 10 位）时「获取验证码」「登录」都是灰的；填满 11 位后「获取验证码」亮起。
5. 点「获取验证码」后按钮变成「60 秒后重发」并逐秒递减，期间不可点。
6. 粘贴带 `+86` 或空格的号码能通过校验（不应该被截断）。
7. 验证码填错 → 状态文字显示「登录失败（11004）：验证码错误」，**不会**切页或关弹窗。
8. 验证码填对 → 弹窗关闭、进「我喜欢的」、右上角账号下拉显示昵称与头像。
9. 切回扫码页签能正常出码并被手机扫到（扫码路径无回归）。
10. Escape 仍然关不掉弹窗。
11. 浅色 / 深色 / 高对比度三种主题下，两个页签与手机号面板都正常。
