# 配置文件统一：单一 config.json

**日期：2026-11-06。状态：阶段 1–4 已实施；阶段 5–6 按保守收尾处理（不删旧接口）。**

起因：设置项一项一个 JSON 文件，目前有 18 份。每加一个设置，往往要同时动模型、
接口、store、JSON 上下文、`AppPaths`、DI 和测试七处。本文给出合并成一份配置文件的结构、
存储层设计、容错策略与迁移方案。

相关：[`settings.md`](settings.md)（设置页与各项偏好的语义）、
[`multi-account.md`](multi-account.md)（设备级 / 个人级 / 账号级三分）、
[`play-queue.md`](play-queue.md)（队列快照，**不在本次合并范围**）。

---

## 1. 结论

1. 格式用 **JSON**，沿用现有 `System.Text.Json` 源生成，不引入新依赖。
2. 合成一份 `%LOCALAPPDATA%\Bowen\config.json`，根对象带 `schemaVersion`，按 section 分组。
   新文件名不用 `settings.json`，因为那已经是旧的主题文件；迁移后旧文件整体归档。
3. **所有读写集中到一个单例 `ISettingsStore`**：启动时同步加载一次，内存里持有整份配置，
   写盘走防抖合并 + 原子替换。文件合并不是难点，**写入合一是**；否则多个 store 各自
   「读整份 → 改一段 → 写整份」会互相覆盖。
4. 新增设置项 = 往对应 section 的类里加一个带默认值的属性。不需要新文件、新 store、
   新 JSON 上下文、新 DI 注册。
5. 凭据、账号级数据、设备标识、缓存与日志**不并进来**，理由见 §9。

---

## 2. 目标与非目标

### 目标

- 18 份设置文件合成 1 份，目录里不再堆积 `xxx.json`。
- 加字段即生效：缺字段自动回落默认值，不需要在每个 store 里补 `Has(...)`。
- 老版本升级上来，现有设置全部保留，迁移幂等、可重复执行。
- 读失败、写失败、单字段类型错误都不影响应用启动，也不删用户文件。
- 保持现有 `I*SettingsStore` 接口可用，迁移分阶段落地，不要求一次改完所有调用点。

### 非目标

- 不做云同步、不做跨设备同步。
- 不合并账号级数据（队列、历史、搜索历史）与凭据。
- 不改 `devid.txt` / `session.dat` 的路径与格式，探针共用约束不变。
- 不为旧版本提供「读新文件」的向后兼容；迁移是单向的。
- 不做运行时文件监听（`FileSystemWatcher`）。设置只由本进程写。

---

## 3. 现状

### 3.1 文件清单

| 文件 | 模型 | 当前 store |
| --- | --- | --- |
| `settings.json` | `ThemeSettings` | `JsonThemeSettingsStore` |
| `font-settings.json` | `FontSettings` | `JsonFontSettingsStore` |
| `shell-background.json` | `ShellBackgroundSettings` | `JsonShellBackgroundSettingsStore` |
| `playback.json` | `PlaybackSettings` | `JsonPlaybackSettingsStore` |
| `volume.json` | `VolumeSettings` | `JsonVolumeSettingsStore` |
| `audio-quality.json` | `AudioQuality`（裸枚举） | `JsonAudioQualitySettingsStore` |
| `audio-effects.json` | `AudioEffectSettings` | `JsonAudioEffectSettingsStore` |
| `lyrics.json` | `LyricsSettings` | `JsonLyricsSettingsStore` |
| `desktop-lyrics.json` | `DesktopLyricsSettings` | `JsonDesktopLyricsSettingsStore` |
| `lyrics-poster.json` | `LyricPosterSettings` | `JsonLyricPosterSettingsStore` |
| `view-mode.json` | `ViewModeSettings` | `JsonViewModeSettingsStore` |
| `shortcuts.json` | `ShortcutSettings` | `JsonShortcutSettingsStore` |
| `update.json` | `UpdatePreferences` | `JsonUpdatePreferencesStore` |
| `proxy.json` | `ProxySettings` | `JsonProxySettingsStore` |
| `input-method.json` | `InputMethodSettings` | `JsonInputMethodSettingsStore` |
| `window.json` | `WindowPlacement?` | `JsonWindowPlacementStore` |
| `desktop-lyrics-window.json` | `WindowPlacement?` | `DesktopLyricsPlacementStore` |
| `mini-player-window.json` | `WindowPlacement?` | `MiniPlayerPlacementStore` |

### 3.2 为什么当初拆开

每条拆分理由里，真正成立的只有两类：

- **安全边界**：凭据必须单独、必须 DPAPI 加密；这条仍然成立。
- **数据作用域**：账号级数据换号要跟着换；这条仍然成立。

「写入频率不同」「一个坏字段不该拖累另一个」这两条，在并入统一文件后可以靠
**内存单例 + 防抖 + 分层容错**解决，不再需要用拆文件来回避。逐文件注释里的
「混在一起只会让读写互相牵制」，成立的前提是「每个 store 各自读写整份文件」；
采用 §7 的单写入口后，这个前提消失。

---

## 4. 格式选型

| 格式 | 结论 |
| --- | --- |
| **JSON** | **采用**。已有 `System.Text.Json` + 源生成，零新依赖；嵌套 section、数组、枚举都自然；默认忽略未知字段，向前兼容免费；可开启注释与尾逗号读取，用户手改时更宽容 |
| TOML | 手写更舒服，但要引 Tomlyn，源生成不可用；快捷键那种对象数组反而难写；体积敏感的项目不值得 |
| INI | 无类型、无嵌套、无数组，枚举和结构体只能拼字符串，排除 |
| YAML | 依赖重、缩进敏感，单人维护的项目不值得 |
| SQLite | 原子写和高频写最好，但不能手改，配置只有几十 KB，复杂度与体积都过头 |

JSON 不支持注释是一个小缺点。读取端设 `ReadCommentHandling = Skip` 与
`AllowTrailingCommas = true`，用户手改时可以写注释；写入端仍然输出纯 JSON，
注释会在下次保存时丢失。这可以接受。

---

## 5. 文件结构

### 5.1 路径与命名

- 路径：`%LOCALAPPDATA%\Bowen\config.json`。
  不用 `settings.json`：那已经是旧的主题文件，沿用会让「读旧文件」和「读写新文件」
  落在同一个路径上，迁移逻辑平白复杂。新名字也让归档规则干净 —— 旧文件全部搬走。
- 顶层字段 `schemaVersion`，当前为 `1`。它只标记**结构**版本；只加字段不升版本。
- section 属性用 `camelCase` 写盘。
- 所有 section 都是对象，不把标量直接挂在根上。这样以后给某一项加子字段时，
  不需要把 `"Lossless"` 改成 `{ "preferred": "Lossless" }` 这种不兼容变更。

### 5.2 示例

```json
{
  "schemaVersion": 1,
  "appearance": {
    "theme": "System",
    "acrylicEffects": true,
    "startupPage": "Favorites"
  },
  "fonts": {
    "fontFamily": ""
  },
  "shellBackground": {
    "mode": "CoverAmbient"
  },
  "playback": {
    "mode": "Sequential",
    "restoreQueue": true
  },
  "volume": {
    "level": 100
  },
  "audioQuality": {
    "preferred": "Lossless"
  },
  "audioEffects": {
    "enabled": false,
    "presetId": "",
    "ambientId": "",
    "ambientVolume": 30,
    "eqBands": null
  },
  "lyrics": {
    "showTranslation": true,
    "fontFamily": "",
    "fontSize": 48
  },
  "desktopLyrics": {
    "isEnabled": false,
    "fontSize": 42
  },
  "lyricPoster": {},
  "viewMode": {
    "useGrid": false
  },
  "shortcuts": {
    "bindings": [
      { "action": "TogglePlayPause", "key": "Space", "modifiers": "None" }
    ]
  },
  "update": {
    "autoCheck": true,
    "skippedVersion": ""
  },
  "proxy": {
    "mode": "Direct",
    "address": ""
  },
  "inputMethod": {
    "compatibilityEnabled": false
  },
  "windows": {
    "main": { "x": 0, "y": 0, "width": 1280, "height": 800 },
    "desktopLyrics": null,
    "miniPlayer": null
  }
}
```

示例只列了部分字段，实际 section 保留现有模型的全部字段。

### 5.3 根模型

```csharp
public sealed record AppSettings
{
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ThemeSettings Appearance { get; set; } = new();
    public FontSettings Fonts { get; set; } = new();
    public ShellBackgroundSettings ShellBackground { get; set; } = new();
    public PlaybackSettings Playback { get; set; } = new();
    public VolumeSettings Volume { get; set; } = new();
    public AudioQualityPreferences AudioQuality { get; set; } = new();
    public AudioEffectSettings AudioEffects { get; set; } = new();
    public LyricsSettings Lyrics { get; set; } = new();
    public DesktopLyricsSettings DesktopLyrics { get; set; } = new();
    public LyricPosterSettings LyricPoster { get; set; } = new();
    public ViewModeSettings ViewMode { get; set; } = new();
    public ShortcutSettings Shortcuts { get; set; } = new();
    public UpdatePreferences Update { get; set; } = new();
    public ProxySettings Proxy { get; set; } = new();
    public InputMethodSettings InputMethod { get; set; } = new();
    public WindowSettings Windows { get; set; } = new();
}
```

新增两个小模型：

```csharp
public sealed record AudioQualityPreferences
{
    public AudioQuality Preferred { get; set; } = AudioQuality.Lossless;
}

public sealed record WindowSettings
{
    // null 表示从未保存过，窗口按默认位置打开。
    public WindowPlacement? Main { get; set; }
    public WindowPlacement? DesktopLyrics { get; set; }
    public WindowPlacement? MiniPlayer { get; set; }
}
```

---

## 6. 模型形状：必须是 `get; set;`，不能是 `init`

这是本文最容易踩错的一点，单独写。

### 6.1 实测

2026-11-06 用 .NET 10 临时工程验证了源生成器对缺字段的处理：

| 模型形状 | `{}` 反序列化结果 |
| --- | --- |
| `record` + `{ get; init; }` + 初始化器 | **CLR 默认值**（`null` / `0`），初始化器不参与 |
| `class` + `{ get; init; }` + 初始化器 | 同上 |
| `record` + `{ get; set; }` + 初始化器 | **初始化器的值** |
| `class` + `{ get; set; }` + 初始化器 | 初始化器的值 |

源生成器对 `init` 属性走「有参构造」快路径
（现有测试注释里提到的 `ObjectWithParameterizedConstructorCreator`），
文件里缺的字段被塞 `default`。显式加无参构造、加 `[JsonConstructor]` 都改不了这条路径。
只有可写属性（`set`）才会走无参构造，属性初始化器才会生效。

`tests/Bowen.Core.Tests/LyricsSettingsStoreTests.cs` 里那条
`MissingField_FallsBackToTheClrDefault_NotTheInitializer` 钉住的就是这个行为；
统一后要把它反过来，改成「缺字段落到初始化器」。

### 6.2 规则

**所有会出现在 `config.json` 里的模型，属性一律 `{ get; set; }`，不用 `init`。**

- 保留 `record`：`with` 表达式、值相等、测试断言都不受影响。
- 保留静态 `Default` 与 `Normalized()`：`Normalized()` 继续用 `with` 返回新实例。
- 现有 8 个位置参数 record 改成「无参 + 属性初始化器」：
  `ThemeSettings`、`PlaybackSettings`、`VolumeSettings`、`AudioEffectSettings`、
  `UpdatePreferences`、`ProxySettings`、`InputMethodSettings`、`WindowPlacement`。
- 位置参数 record 的缺字段行为取决于构造参数默认值，不统一；
  改成属性初始化器后，默认值只有一处，新增字段只加属性。
- `ShortcutBinding` 例外：它在数组里，缺字段会得到 `default` 枚举值，
  由 `ShortcutSettings` 的校验统一剔除，不按这条规则逐个补默认值。

### 6.3 副作用

`init` 改成 `set` 后，属性可被任何持有者直接改写。约束放在使用侧：

- `ISettingsStore.Current` 只读用，改配置必须走 `Update`；
- 现有的 `I*SettingsStore.Save` 适配器继续负责「改完落盘」；
- 代码评审时把「直接改 `Current.Xxx.Yyy` 不调 Save」当作缺陷。

这是可接受的代价：这些类型本来就是持久化形状，不是并发共享的业务对象。

---

## 7. 存储层设计

### 7.1 接口

```csharp
public interface ISettingsStore
{
    /// <summary>当前内存副本。只读使用；修改必须走 Update。</summary>
    AppSettings Current { get; }

    /// <summary>改内存并安排防抖落盘。失败只记日志。</summary>
    void Update(Action<AppSettings> mutate);

    /// <summary>改内存并立即落盘，返回是否成功。失败敏感的开关用它。</summary>
    bool UpdateAndFlush(Action<AppSettings> mutate);

    /// <summary>把待写内容立即写盘。退出、重启、切号前调用。</summary>
    bool Flush();
}
```

`JsonAppSettingsStore` 实现要点：

- 构造时接收一个已加载的 `AppSettings` 与日志器；加载由 §10 的 bootstrap 完成。
- `Update` 在锁内执行 `mutate(Current)`，标脏，重置 500 ms 定时器。
- 定时器到点后在后台线程序列化并写盘；写盘失败只记 warning，保留脏标记等下次。
- 写盘串行化：同一时刻只有一个写操作；写期间又有更新，结束后补写一次。
- `UpdateAndFlush` 在锁外立即写，返回结果。写入失败时不回滚内存值
  （与现有「本次会话已生效」的语义一致），由调用方决定是否回滚界面状态。
- `Flush` 取消定时器并同步写。正常退出、托盘退出、重启前必须调用。
- 序列化走源生成 `AppSettingsJsonContext`，不开运行时反射。

### 7.2 为什么必须单写入口

合并文件后，任何「读文件 → 改一段 → 写文件」的旧写法都会丢更新：

```
A 读到 { appearance: X, volume: 10 }
B 读到 { appearance: X, volume: 10 }
A 写 { appearance: Y, volume: 10 }
B 写 { appearance: X, volume: 20 }   // A 的修改被覆盖
```

单例 + 内存副本 + 所有修改走 `Update`，从根上消除这类竞争。这也是迁移阶段
**必须先接线、再逐个替换调用点**的原因；不能反过来。

### 7.3 防抖

音量拖动、窗口移动是高频写。现有音量 store 上游已有 1 秒防抖，窗口几何也有防抖；
store 内部再加 500 ms 合并，保证一次拖动最多写几次。

文件只有几十 KB，整份重写代价可以忽略；真正要避免的是每像素一次写盘。

### 7.4 启动时加载

`Program.Main` 在 `Application.Start` 之前同步调用 `SettingsBootstrap.Load()`：

```csharp
var bootstrap = SettingsBootstrap.Load();   // 同步、含迁移
var imeEnabled = bootstrap.Settings.InputMethod.CompatibilityEnabled
    || args.Contains("--ime-compatibility", StringComparer.OrdinalIgnoreCase);
```

这样输入法兼容、界面字体这些「必须在 XAML 初始化前就绪」的设置都从同一份内存对象取，
不再各开一次文件。bootstrap 阶段的日志走 `Program.Report`（此时 Serilog 还没接上）。

### 7.5 退出时写入

- 托盘「退出」、窗口关闭到托盘后再退出、重启进程：都调 `Flush()`。
- 强杀进程时最多丢最后一次防抖窗口内的改动；这是可接受的。
- 窗口几何在关闭前本来就要求落盘，适配器里调 `UpdateAndFlush`，不依赖定时器。

### 7.6 失败反馈

现有部分接口的 `Save` 返回 `bool`（音量、输入法兼容等），调用方据此回滚界面开关。
适配器映射规则：

- 失败不敏感、频率高（音量、窗口几何）→ `Update`，返回 `true`；
- 失败敏感（输入法兼容、快捷键）→ `UpdateAndFlush`，把真实结果返回给调用方。

---

## 8. 容错策略

分四层，坏一处不拖垮整份：

| 层 | 失败形态 | 处理 |
| --- | --- | --- |
| 1. 根文件 | 文件损坏、不是 JSON | 先试 `config.json.bak`；都失败则全默认，保留坏文件 |
| 2. section | 某个 section 类型不对（字符串给了对象等） | **只重置该 section**，其它 section 保留 |
| 3. 字段 | 缺字段 | 属性初始化器自动补默认值（§6） |
| 4. 字段值 | 未知枚举名、越界数字 | 容错枚举转换器 → 默认值；`Normalized()` 逐项夹取 |

第 2 层实现：先 `JsonDocument.Parse` 根对象，再按 section 逐个
`Deserialize`，单个 section 抛 `JsonException` 只影响它自己。不要整份一次
`Deserialize<AppSettings>`，那样一个坏 section 会重置全部。

第 4 层的枚举转换器：

- 未知字符串、未定义数字 → 返回该枚举登记的默认值；
- 每个枚举在中心表里登记默认值（例如 `AppTheme.System`、`PlayMode.Sequential`）；
- 没有登记的枚举回退 `default(T)`，并由对应 section 的 `Normalized()` 再挡一道。

`JsonStringEnumConverter` 遇到未知字符串会直接抛异常，数字却会读成未定义值，
两个方向都要挡。

### 8.1 坏文件保留

- 读失败**不删除**文件，只记日志，与现有各 store 一致。
- 若主文件损坏、走了 `.bak` 或默认值，**下次写入前**先把主文件复制成
  `config.json.corrupt-yyyyMMddHHmmss` 再覆盖，避免 `File.Replace` 的备份
  把好的 `.bak` 冲掉。
- 结构无法识别（没有 `schemaVersion`、或版本高于当前）同样先改名保留，
  不直接覆盖；这种文件可能来自更新版本，也可能是手改坏的。
- `.corrupt-*` 文件不自动清理，留给人看。数量多时在存储页加一条手动清理，
  本次不做。

### 8.2 原子写

沿用现有写法，并升级为带备份的替换：

1. 序列化到 `config.json.tmp`；
2. 目标存在 → `File.Replace(tmp, config.json, config.json.bak)`；
3. 目标不存在 → `File.Move(tmp, config.json)`；
4. 失败 → 记 warning，删掉 `.tmp` 的尝试不抛异常。

`File.Replace` 在 Windows 上是原子的，并且自带备份。

---

## 9. 不并入统一文件的数据

| 数据 | 位置 | 原因 |
| --- | --- | --- |
| 会话凭据 | `session.dat` | DPAPI 加密，与探针共用，格式是对外契约 |
| 记住的账号 | `remembered-accounts.dat` | DPAPI 加密，与 session 的 entropy 不同 |
| 设备标识 | `devid.txt` | 与探针共用；换路径等于换设备，触发账号风控 |
| 播放队列 | `accounts\<scope>\queue.json` | 账号级数据，换号要跟着换 |
| 播放历史 | `accounts\<scope>\history.json` | 同上 |
| 搜索历史 | `accounts\<scope>\search-history.json` | 同上 |
| 封面缓存 | `cache\covers\` | 可再生缓存 |
| 音频缓存 | `cache\audio\` | 可再生的临时目录 |
| 背景图片 | `backgrounds\` | 资源文件，不是配置 |
| 环境音 | `ambient\` | 程序生成的可再生资源 |
| 日志 | `logs\` | 不是配置 |
| 安装包 | `updates\` | 不是配置 |

窗口几何（三份）**并入** `windows` section。它们确实属于「这台机器」，
但同属配置；高频写由防抖处理，不需要为它保留独立文件。

---

## 10. 迁移

### 10.1 判定顺序

启动时 `SettingsBootstrap.Load()`：

1. `config.json` 存在：
   - 解析成功且 `schemaVersion` 等于当前版本 → 直接用，不再读旧文件；
   - 解析成功但没有 `schemaVersion`，或版本高于当前版本 → 不猜测它的含义，
     先改名为 `config.json.unrecognized-yyyyMMddHHmmss` 保留，再按「无有效统一文件」迁移；
   - 解析失败 → 试 `config.json.bak`；`.bak` 有效就用 `.bak`；
     `.bak` 也失败 → 全默认，标记「主文件损坏」。
2. `config.json` 不存在 → 不读 `.bak`，直接进入旧文件迁移。
3. 迁移只发生在「没有有效的统一文件」时；一旦写入了 `schemaVersion`，
   后续启动不再导入旧文件，只做 §10.4 的归档兜底。

### 10.2 旧文件映射

| 旧文件 | section |
| --- | --- |
| `settings.json`（旧的 `ThemeSettings`） | `appearance` |
| `font-settings.json` | `fonts` |
| `shell-background.json` | `shellBackground` |
| `playback.json` | `playback` |
| `volume.json` | `volume` |
| `audio-quality.json` | `audioQuality.preferred` |
| `audio-effects.json` | `audioEffects` |
| `lyrics.json` | `lyrics` |
| `desktop-lyrics.json` | `desktopLyrics` |
| `lyrics-poster.json` | `lyricPoster` |
| `view-mode.json` | `viewMode` |
| `shortcuts.json` | `shortcuts` |
| `update.json` | `update` |
| `proxy.json` | `proxy` |
| `input-method.json` | `inputMethod` |
| `window.json` | `windows.main` |
| `desktop-lyrics-window.json` | `windows.desktopLyrics` |
| `mini-player-window.json` | `windows.miniPlayer` |

### 10.3 复用现有 `Load()`，不要重写解析

旧文件里有几处历史格式：

- `playback.json` 老版本是裸枚举，新版本是对象；
- `lyrics.json` 缺外观字段时要按「文件里有没有这个属性」补默认；
- 音质是裸枚举，越界要回默认。

这些逻辑已经在各自的 `Load()` 里。迁移**直接调用现有 store 的 `Load()`**，
把结果赋给 section，再写统一文件。不要为了迁移另写一套解析，否则两套会漂移。

旧 store 在迁移阶段保留；调用点全部迁完后，再把它们的 `Load()` 收进
`LegacySettingsReader`（只读），`Save` 与接口可以删。迁移代码长期保留，
因为任何老版本用户升级上来都要走一次。

### 10.4 旧文件归档

迁移**写盘成功后**，把 18 份旧设置文件移动到归档目录，不留在根目录：

```text
%LOCALAPPDATA%\Bowen\archive\legacy-settings\
  settings.json
  font-settings.json
  ...
  README.txt
```

规则：

- 目标目录已存在且含同名文件时，改用 `legacy-settings-yyyyMMdd-HHmmss`，
  不覆盖上一次归档。
- 只移动 §10.2 表里的 18 份文件。凭据、账号级数据、缓存、日志一概不动。
- 单个文件移动失败（被占用、权限不足）只记 warning，继续移动其它文件；
  下次启动若 `config.json` 有效且旧文件还在，再尝试一次。
归档完成后在目录里写 `README.txt`，内容如下：

```text
这些是旧版设置文件，已迁移到应用数据目录的 config.json。
新版本使用正常的话，可以删除本目录；
需要回退到旧版本时，先把这些文件放回应用数据目录。
```

应用不自动删除归档目录，清理由用户决定。

### 10.5 幂等与失败

- 迁移可重复执行：有 `schemaVersion` 就不再导入。
- 某个旧文件损坏：该 section 用默认值，继续迁移其它文件，不整体失败。
- 写统一文件失败：本次会话用内存值继续跑，下次启动再迁移一次；
  此时**不移动**任何旧文件。
- 迁移写盘成功前不动旧文件；成功后只移动到归档目录，不删除。

### 10.6 结构版本升级

只有 section 改名、字段搬家、删除字段这类**结构变化**才升 `schemaVersion`。
只加字段永远不升版本，缺字段由初始化器补齐。

升级时按版本号顺序执行迁移函数，例如：

```text
v1: 初始统一格式
v2: 以后若把 appearance.startupPage 移到 startup 段
```

---

## 11. 代码落点

### 11.1 新增

| 文件 | 内容 |
| --- | --- |
| `src/Bowen.Core/Models/AppSettings.cs` | 根模型、`AudioQualityPreferences`、`WindowSettings` |
| `src/Bowen.Core/Services/Abstractions/ISettingsStore.cs` | §7.1 接口 |
| `src/Bowen.Core/Services/Implementations/JsonAppSettingsStore.cs` | 加载 / 防抖 / 原子写 / 备份 |
| `src/Bowen.Core/Services/AppSettingsJsonContext.cs` | 源生成上下文 |
| `src/Bowen.Core/Services/SettingsBootstrap.cs` | 启动加载、主文件 / `.bak` / 旧文件判定 |
| `src/Bowen.Core/Services/SettingsPaths.cs` | 统一配置、备份与归档路径（根目录可注入） |
| `src/Bowen.Core/Services/LegacySettingsCatalog.cs` | 旧 18 份文件的文件名与迁移/归档清单 |
| `src/Bowen.Core/Services/LegacySettingsReader.cs` | 旧 18 份文件的只读解析（复用现有 store 的 `Load()`） |
| `src/Bowen.Core/Services/LegacySettingsArchive.cs` | 迁移成功后把旧文件移入 `archive\legacy-settings` 并写说明 |
| `src/Bowen.Core/Services/TolerantEnumConverter.cs` | 未知枚举值回默认 |

### 11.2 修改

- 8 个位置参数 record 改成属性初始化器（§6.2）。
- 其余设置模型的 `init` 改成 `set`。
- `AppPaths`：
  - 新增 `ConfigFile`，指向 `config.json`；
  - 现有 `SettingsFile`（旧主题文件）与其余 17 个设置文件路径移到
    `LegacySettingsPaths`，只给迁移与归档用；
  - 设备级、账号级、缓存、日志、背景、环境音的路径不动。
- `App.xaml.cs`：
  - 注册 `ISettingsStore`（bootstrap 的实例）；
  - 现有 18 个 `I*SettingsStore` 改为接收 `ISettingsStore` 的适配器；
  - DI 注册数量不变，调用点不动。
- `Program.cs`：XAML 初始化前用 `SettingsBootstrap.Load()` 取输入法设置。

### 11.3 适配器示例

```csharp
public sealed class UnifiedThemeSettingsStore(ISettingsStore store) : IThemeSettingsStore
{
    public ThemeSettings Load() => store.Current.Appearance with { };

    public void Save(ThemeSettings settings) =>
        store.UpdateAndFlush(current => current.Appearance = settings.Normalized());
}
```

迁移期先保留所有接口与适配器。新代码直接注入 `ISettingsStore`；
调用点逐个迁完后，再删无用的接口、适配器与 `LegacySettingsPaths` 里的 `Save` 路径。

---

## 12. 测试清单

### 12.1 模型与序列化

- `{}` 反序列化：所有 section 取初始化器默认值（与现有
  `MissingField_FallsBackToTheClrDefault_NotTheInitializer` 相反，替换那条测试）。
- 缺整个 section：根对象的初始化器新建默认 section。
- 未知字段：忽略，不抛。
- camelCase 写盘；读旧 PascalCase 文件成功（迁移用）。
- 未知枚举字符串 → 登记默认值，其它字段保留。
- 未定义数字枚举 → 登记默认值。
- 越界数字 → `Normalized()` 夹到范围内。

### 12.2 存储层

- 全量 round-trip：写一次、读回来逐 section 相等。
- 原子写：不留 `.tmp`；目标存在时生成 `.bak`。
- 损坏主文件：走 `.bak`；两份都坏 → 默认值，主文件保留。
- 坏 section：只重置该 section。
- 防抖：连续 10 次 `Update` 只写一次（注入时钟或计数）。
- `UpdateAndFlush` 失败返回 `false`，内存值不回滚。
- 并发：多线程 `Update` 不同 section，最终文件包含全部修改。
- `Flush` 取消待写定时器并同步落盘。

### 12.3 迁移

- 18 份旧文件齐全 → 全部字段保留。
- 只存在部分旧文件 → 其余用默认值。
- 旧 `settings.json` 只有主题 → `appearance` 正确，其它默认。
- 旧 `playback.json` 裸枚举 → 正确读成对象。
- 旧 `lyrics.json` 缺外观字段 → 走现有 `Load()` 的补默认逻辑。
- 某个旧文件损坏 → 该 section 默认，其它照常。
- 幂等：第二次启动不再读旧文件；修改旧文件后重启不生效。
- `config.json` 损坏 + `.bak` 有效 → 用 `.bak`，不重新导入旧文件。
- 迁移写盘失败 → 内存可用，下次启动重试，且旧文件不移动。
- 归档：迁移成功后 18 份旧文件出现在 `archive\legacy-settings\`，
  根目录不再有它们；`README.txt` 存在。
- 归档目录已存在 → 新建带时间戳的子目录，旧归档不被覆盖。
- 单个文件移动失败 → 其它文件照常归档，下次启动重试。

### 12.4 兼容现有测试

18 个 `*SettingsStoreTests` 通过适配器继续跑。适配器内部用临时目录的
`ISettingsStore`，不改测试的结构；只改构造方式。

---

## 13. 风险与代价

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| 单文件损坏拖垮全部 | 所有设置回默认 | 分层解析（section 级隔离）、`.bak`、坏文件保留、`.corrupt-*` |
| 多个 store 同时写 | 丢更新 | 单例 + 单写入口；迁移阶段先接存储层再换调用点 |
| `init` 改 `set` 后误改内存 | 改了没落盘 | `Current` 只读约定；适配器统一 `Save`；评审拦住直接赋值 |
| 防抖窗口内强杀 | 丢最后一次改动 | 正常退出 `Flush`；窗口几何等用 `UpdateAndFlush` |
| 迁移解析漂移 | 旧设置丢失或错位 | 复用现有 `Load()`，不另写解析；迁移测试覆盖每种旧格式 |
| 未来跨设备同步 | 整文件同步不合适 | 按 section 选择同步项；本次不实现 |
| 文档大量提到旧文件名 | 文档与实现不一致 | 实施时全仓 grep 一次，集中更新 |

---

## 14. 分阶段实施

| 阶段 | 内容 | 可独立验证 |
| --- | --- | --- |
| 0 | 本文定稿 | — |
| 1 | 模型形状：`init` → `set`，8 个位置参数 record 改成属性初始化器 | 现有测试全过；新增「`{}` 取初始化器」测试 |
| 2 | `AppSettings` + `JsonAppSettingsStore` + 容错解析 + 测试 | 新测试覆盖 §12.1 / §12.2 |
| 3 | `SettingsBootstrap` + `LegacySettingsReader` + `LegacySettingsArchive` + 迁移测试 | §12.3 全过；手工造一套旧文件验证 |
| 4 | DI 接线 + 18 个适配器；`Program.cs` 改用 bootstrap | 应用启动、设置页全部正常；旧文件已归档且不再被写 |
| 5 | 调用点改为直接注入 `ISettingsStore`，删旧接口 / store | 逐步删除，每步构建与测试 |
| 6 | 清理 `AppPaths`、更新 `settings.md` 等文档 | 全仓 grep 旧文件名 |

阶段 1–4 完成即达到「一份文件 + 加字段即可」的目标。

阶段 5–6 **按保守收尾处理**：不删旧接口与适配器，只做注释与文档清理。
理由见 §17：调用点有 56 处，改动收益低于风险；旧 store 的只读解析迁移还要用。

---

## 15. 验收清单

自动：

- `dotnet build` 0 错误；既有 0 警告目标不退化。
- `dotnet test` 全部通过，含 §12 新增用例。
- 迁移测试覆盖「齐全 / 部分 / 主题-only / 损坏 / 幂等」五种。

人工：

- 用当前版本生成一套设置（主题、音量、歌词外观、快捷键、窗口位置、代理、更新），
  换新版本启动：全部保留。
- 根目录新增 `config.json` 并被持续更新；18 份旧文件移入
  `archive\legacy-settings\`，且目录里有 `README.txt`。
- 再改一次设置：归档里的旧文件修改时间不再变化。
- 删掉 `config.json` 重启：全默认，不崩。
- 删掉 `config.json` 和归档目录后重启：全默认，不崩，不会重建旧文件。
- 手改一个 section 为错误类型：只有该 section 回默认，其它保留。
- 把音量从 100 拖到 0：不卡顿，磁盘写入次数明显少于拖动次数。
- 托盘退出再启动：最后一次改动仍在。
- 输入法兼容开关保存失败时，开关回滚到之前状态（失败敏感路径）。

---

## 16. 已确认的决定（2026-11-06）

1. **文件名用 `config.json`。** 不用 `settings.json`，避免与旧主题文件同路径，
   迁移逻辑更干净。
2. **旧文件归档，不删除。** 迁移成功后移到 `archive\legacy-settings\`，
   目录里放 `README.txt` 说明「使用正常即可清理」；应用不自动清理。
3. **防抖 500 ms。** 保留音量与桌面歌词窗口现有的上游定时器，存储层防抖作为兜底；
   音量最坏 1.5 秒落盘，正常退出 `Flush` 不受影响。
4. **窗口几何并入 `windows` section。** 三份独立文件取消。

---

## 17. 实施记录（2026-11-06）

- **阶段 1–4 已完成**：模型形状、统一存储、启动迁移与归档、DI 接线和 18 个适配器。
- **保守收尾**：18 个旧接口与适配器保留为兼容层。调用点有 56 处，全部改成直接注入
  `ISettingsStore` 的收益低于改动风险；旧 store 仍被 `LegacySettingsReader`（只读）
  和既有测试使用，因此不删。
- `AppPaths` 里的 18 个旧设置路径已标注为「只用于迁移和旧 store 默认值」；
  文档中的旧文件名已更新为 `config.json` 的 section。
- 自动验证：Core / WinUI 构建 0 警告 0 错误；离线测试 1877 项通过。
- **待人工验收**：升级路径（设置保留、`config.json` 生成、旧文件归档到
  `archive\legacy-settings\`）、各项设置往返、托盘退出后最后一次改动仍在。
