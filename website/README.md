# 项目介绍站

波纹音乐的介绍页。Astro 静态站，无任何集成（没有 Tailwind、没有前端框架），产物是纯 HTML + 一份 CSS。

## 开发

```bash
cd website
npm install
npm run dev      # http://localhost:4321/bowen_music/
npm run build    # 产出 dist/
npm run preview  # 起服务看构建后的结果
```

开发地址带 `/bowen_music/` 前缀，因为 `astro.config.mjs` 里配了 `base`，与 GitHub Pages 项目页的路径一致。前缀不一致会导致构建产物里的资源引用全部 404，所以本地就按真实路径跑。

## 视觉基准：WinUI 3 / Fluent

界面按 Fluent Design（Windows 11）的规矩做，**不是通用的网页那套毛玻璃**。改动样式前先看这几条，破了一条就不像了：

1. **平面化。** 层次靠分层填充 + 1px 描边表达，不靠投影。投影只给真正浮动的东西（窗口截图、浮出层、Hero 图标）。
2. **小圆角。** 控件 4px，卡片与对话框 8px。圆角一大就成了网页卡片。
3. **小字号。** 正文 14px、行高 20px；标题走 WinUI 的 Type Ramp（Caption 12 / Subtitle 20 / Title 28 / TitleLarge 40）。
4. **材质分工。** Mica 做窗口底（整页那层），Acrylic 只给需要透出下层的浮层（吸顶栏、页脚）。给每张卡片都上 `backdrop-filter` 是网页的玻璃拟态，不是 Fluent。
5. **强调色克制。** 只在主按钮、选中指示条、图标上出现。
6. **色彩令牌是 Fluent 的语义色**，写在 `:root` 与 `html[data-theme='dark']` 里。别自己调一组"好看的颜色"，改就用 Fluent 的原值。

### 背景的两层结构

页面底部是两层，都在 `Base.astro` 里：

| 层 | 类名 | 作用 |
| --- | --- | --- |
| 壁纸层 | `.wallpaper` | 静态斜向 wash + 四团缓慢漂移的光斑。Mica 的取色来源，**刻意压得淡** —— 它要像壁纸，不能像霓虹 |
| Mica 层 | `.mica` | 半透明中性填充 + Acrylic 噪点。噪点是 Fluent 材质的关键，少了它一眼就假 |

光斑是纯 `radial-gradient`，没用 `filter: blur`，避免大面积模糊的开销。四个光斑的中心都压在视口内 —— 中心跑到视口外就只剩淡出的边缘，颜色会看不见。周期 30–46 秒，`prefers-reduced-motion` 下全部停掉。

## 改内容

**所有事实都集中在 `src/data/site.ts`** —— 版本号、下载产物体积、依赖表、链接、截图编排、功能条目。
组件里不写死任何事实，改版时只改这一个文件。

组件对应关系：

| 组件 | 区块 |
| --- | --- |
| `TitleBar.astro` | 顶栏：应用图标 + 名称、主导航、主题切换 |
| `Hero.astro` | 图标、名称、一句话介绍、下载按钮、三枚徽章，以及主界面截图 |
| `Pains.astro` | 起因：官方 PC 端缺什么，三条 |
| `Lyrics.astro` | 歌词线三条 + 两张歌词截图 |
| `Features.astro` | 功能，一格十行，一行一句 |
| `Size.astro` | 体积对比（−93.1% 与两根条） |
| `Shots.astro` | 收尾的一行三张截图（MV / 搜索 / 小窗） |
| `GetStarted.astro` | 上手三步 + 一行快捷键 |
| `Download.astro` | 两个产物卡片 + 一行说明与校验 |
| `Notes.astro` | 已知限制 + 一句隐私 + 一句声明 |
| `AppWindow.astro` | 应用窗口外框（标题栏 + 16:10 画面 + 缺图占位） |
| `Icon.astro` | 图标集，接 Fluent System Icons |

## 页面结构

结构以 `ldm0715.github.io/emobox` 为准（2026-10-09 重排）：

- **每个 h2 是一句主张，不是目录标签**（「官方 PC 端缺的，这里都有」，而不是「起因」）。
  改标题时保持这个写法。
- **每节只讲一件事**，导语只有一句。
- **能一行说清的不写成卡片**：功能是十行细线列表，不是十张卡；快捷键是一行，不是五张键位卡。
- **每节一个视觉焦点**：Hero 一张大图，歌词节两张，收尾一行三张。别把六张截图铺满一屏。

被砍掉的东西和去向：从源码构建那节（说明留在仓库 README 与 `docs/`）；第三方依赖表
（缩成页脚一行链接，指向 `src/Directory.Packages.props`）；隐私四条与声明（各缩成一句）。

样式全在 `src/styles/global.css`。主题靠 `html[data-theme]` 切换，默认跟随系统，用户手动切过之后记在 `localStorage`。

### 图标

`Icon.astro` 接的是 **Fluent System Icons**（微软官方，MIT），来自 `@fluentui/svg-icons`
（devDependency，构建期读一次，产物里只内联真正用到的那几个）。

- 用 `?raw` **逐个静态导入**，不要改成 `import.meta.glob` —— 那个包里有 20793 个图标文件，
  glob 会把它们全拖进构建。
- 组件把 viewBox 提出来、剥掉外层 `<svg>`、把写死的 `fill="#212121"` 换成 `currentColor`，
  颜色一律由 CSS 管。所以加新图标时**不用管颜色**，只要保证 `fill` 不是 `none`。
- Fluent 图标是**填充式字形**，不是描边，别按 stroke 的思路调。
- 尺寸按 16 / 20 / 24 的网格走，`width: 13px` 这种会糊。
- 语义键名（`comment` / `wave` …）到 Fluent 文件名的映射写在 `Icon.astro` 的 `fluent` 表里。
  **GitHub 是品牌标志**，微软不收品牌 logo，那一枚是手写的。

## 放截图

见 `SCREENSHOTS.md`。**统一 16:10（1440 × 900）**，窗口外框的比例是固定的。

## 内容来源

页面上的每条事实都对应仓库里的出处，写文案时不要凭印象加：

| 出处 | 用在哪 |
| --- | --- |
| `README.md` | 特性、下载体积、系统要求、上手步骤、快捷键、隐私、限制、依赖表 |
| `changes.md` | v0.1.0 的发布说明（当前页面的版本号与日期） |
| `docs/roadmap.md` | 阶段状态、长期维护事项 |

改版本号时，`site.ts` 里的 `version` / `versionDate` 要跟 `src/Directory.Build.props` 和 `changes.md` 对齐。

## 规范

- **不写营销文字**。陈述事实，不用「极速」「极致」「轻松搞定」「强大」这类词。
- **文字从简**。能用图标和截图说清的就不写字；功能条目的说明控制在一行以内。
- 数字、体积、版本号一律取自仓库，不估算、不四舍五入到好看的值。
- 非官方声明与 GPL 相关表述保持与 `README.md` 一致。

## 部署

GitHub Pages，通过 Actions 部署 `dist/`。工作流文件在仓库根的 `.github/workflows/`。

`astro.config.mjs` 里的 `base` 与仓库名绑定。**仓库改名或绑自定义域名时这一行必须同步改**，否则资源全 404。
