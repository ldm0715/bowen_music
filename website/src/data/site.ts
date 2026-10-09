/**
 * 站点全部事实的唯一来源。
 * 每条都能在仓库里找到出处：README.md、changes.md、docs/roadmap.md、docs/release.md。
 *
 * 文案规矩（照 ldm0715.github.io/emobox 的结构）：
 *   - 每个小节的标题是一句主张，不是目录标签
 *   - 导语只有一句
 *   - 能一行说清的不写成卡片；数字、体积、版本号一律取自仓库
 */

/**
 * public/ 下的资源要手动拼上 base，Astro 不会改写 public 资源的路径。
 * BASE_URL 的尾斜杠不保证存在，所以两边都归一化，
 * 否则会拼出 /bowen_musiclogo.png 这种缺斜杠的路径。
 */
export const asset = (path = '') => {
  const base = import.meta.env.BASE_URL.replace(/\/+$/, '');
  const rest = path.replace(/^\/+/, '');
  return rest ? `${base}/${rest}` : `${base}/`;
};

export const site = {
  name: '波纹音乐',
  latinName: 'Bowen Music',
  tagline: '把官方 PC 端缺的评论、收藏歌单补上，并把播放状态交给系统媒体面板。',
  repo: 'https://github.com/ldm0715/bowen_music',
  releases: 'https://github.com/ldm0715/bowen_music/releases',
  latestRelease: 'https://github.com/ldm0715/bowen_music/releases/latest',
  issues: 'https://github.com/ldm0715/bowen_music/issues',
  packages: 'https://github.com/ldm0715/bowen_music/blob/main/src/Directory.Packages.props',
  version: '0.1.0',
  versionDate: '2026-10-08',
  license: 'GPL-3.0',
  copyright: '© 2026 gcnanmu',
  platform: 'Windows 10 / 11（x64）',
  architecture: 'x64',
};

/** 顶栏导航。锚点与 index.astro 各 section 的 id 对应，图标名对应 Icon.astro。 */
export const nav = [
  { href: '#why', label: '起因', icon: 'alert' },
  { href: '#features', label: '功能', icon: 'grid' },
  { href: '#shots', label: '界面', icon: 'image' },
  { href: '#download', label: '下载', icon: 'download' },
];

/** Hero 徽章。三枚，每枚是「图标 + 标签 + 值」。 */
export const heroBadges = [
  { icon: 'shield', accent: true, label: 'GPL-3.0 开源' },
  { icon: 'windows', label: 'Windows 10 / 11（x64）' },
  { icon: 'blocks', label: 'WinUI 3 + .NET 10 + libmpv' },
];

/** 起因：官方 PC 端缺什么。取自 README「为什么做这个」。 */
export const pains = {
  title: '官方 PC 端缺的，这里都有',
  lead: '波点音乐的官方 PC 端没有评论、没有收藏歌单，播放状态也不对系统暴露 —— 第三方歌词工具因此读不到你在听什么。',
  items: [
    { title: '没有评论', line: '刷到一首歌想看看别人怎么说，客户端里没有入口。' },
    { title: '没有收藏歌单', line: '在手机端收藏的歌单，PC 端看不到。' },
    { title: '读不到播放状态', line: 'Lyricify 这类工具拿不到歌曲信息，状态栏是空的。' },
  ],
};

/** 歌词线。是项目的原始动机，单独成节。 */
export const lyrics = {
  title: '歌词是主业，不是附赠',
  lead: '这个项目从第一天就是为歌词做的：全屏沉浸页、独立的桌面歌词条，再把播放状态交给系统。',
  items: [
    { icon: 'lyrics', title: '全屏沉浸歌词', line: '逐字渐变、真实音频频谱、封面倒影，点歌词可跳转' },
    { icon: 'pip', title: '桌面歌词', line: '独立悬浮条，可穿透、可锁定、贴边吸附' },
    { icon: 'monitor', title: '系统媒体控制', line: '状态与封面交给系统面板，第三方歌词工具直接读得到' },
  ],
};

/**
 * 截图位。文件名与 README.md 的注释一致，尺寸 1440×900（16:10）。
 * 图放进 website/public/screenshots/ 即可，不用改代码；
 * 图不存在时窗口里显示占位，构建不受影响。
 *
 * 编排原则：Hero 放一张门面，歌词那节配两张，剩下的收成一行三张。
 * 截图铺满一屏反而看不清重点。
 */
export const screenshots = {
  hero: { file: 'main.png', title: '主界面', caption: '侧栏曲库、列表工具栏与底部播放器' },
  lyrics: [
    { file: 'lyrics.png', title: '全屏歌词', caption: '逐字渐变、真实频谱与封面倒影' },
    { file: 'desktop-lyrics.png', title: '桌面歌词', caption: '独立悬浮条，贴边吸附' },
  ],
};

/** 收尾的一行三张。 */
export const gallery = {
  title: '还有这些',
  lead: 'MV、搜索、小窗 —— 同一套外壳里的另外三页。',
  items: [
    { file: 'mv.png', title: 'MV 播放', caption: '全窗沉浸，四种比例' },
    { file: 'search.png', title: '搜索', caption: '综合结果与分类页签' },
    { file: 'mini-player.png', title: '小窗', caption: '贴边自动收起' },
  ],
};

/** 功能。一格十行，一行一句，不分小组、不加标签。 */
export const features = {
  title: '剩下的都在这',
  lead: '',
  items: [
    { icon: 'comment', title: '评论与回复', line: '看、发、赞、回复，含楼中楼' },
    { icon: 'heart', title: '四处收藏', line: '歌曲、歌单、专辑、歌手' },
    { icon: 'library', title: '曲库与歌单', line: '新建、编辑、删除，含最近播放' },
    { icon: 'checklist', title: '列表多选', line: '批量加入喜欢、加入歌单' },
    { icon: 'film', title: 'MV 播放', line: '全窗沉浸，四种画面比例' },
    { icon: 'queue', title: '播放队列', line: '顺序、循环、随机三种模式' },
    { icon: 'sliders', title: '三档音质', line: '标准 / HQ / SQ 随手切' },
    { icon: 'mini', title: '小窗与托盘', line: '贴边收起，关闭到托盘' },
    { icon: 'keyboard', title: '应用内快捷键', line: '不抢别的软件的组合键' },
    { icon: 'user', title: '登录与多账号', line: '手机号或扫码，不登录也能浏览' },
  ],
};

/** 体积。取自 README「特性」最后一条。 */
export const size = {
  title: '音频库只剩 7.97 MiB',
  lead: '自建的精简 libmpv 只保留解码与输出必需的部分，随包分发，不需要额外安装播放器或编解码包。',
  before: { label: '原始 libmpv 音频库', value: '115.22 MiB' },
  after: { label: '自建精简构建', value: '7.97 MiB' },
  drop: '−93.1%',
};

/** 下载。体积为 2026-10-08 实测值，见 README「下载安装」。 */
export const download = {
  title: '下载',
  lead: '两个包内容一样，都自带 .NET 运行时，装完直接能用。',
  plans: [
    {
      name: '安装版',
      file: 'BowenMusic_{v}_x64_setup.exe',
      archive: '49.8 MiB',
      unpacked: '189.8 MiB',
      note: '向导式安装，自动创建开始菜单与桌面快捷方式。不确定就选它。',
      pick: true,
    },
    {
      name: '便携版',
      file: 'BowenMusic_{v}_x64_portable.zip',
      archive: '69.7 MiB',
      unpacked: '189.8 MiB',
      note: '解压即用，适合免安装场景。',
      pick: false,
    },
  ],
  /** 第三张卡降成一行，省掉一块视觉噪音 */
  variant: '还有**无运行时便携版**，37.0 MiB —— 体积少一半，需要目标机器已装 .NET 10 运行时。',
  checksum: 'certutil -hashfile BowenMusic_{v}_x64_setup.exe SHA256',
};

/** 上手。三步，一句话说清。 */
export const start = {
  title: '按一次就能用',
  lead: '',
  steps: [
    { name: '登录', line: '手机号或扫码，不想登录就直接浏览' },
    { name: '找歌', line: '顶部搜索框回车，或翻热榜' },
    { name: '听歌与歌词', line: '点一首即播放，点封面进全屏歌词' },
  ],
  keys: '快捷键：Space 播放暂停 · Ctrl+← → 上下首 · Ctrl+L 收藏，可在设置里改键。',
};

/** 已知限制。取自 README「已知限制」与 changes.md 的 0.1.0 条目。 */
export const limits = {
  title: '已知限制',
  lead: '',
  items: [
    '下载功能尚未实现（计划中）。官方 PC 端有下载，这个客户端暂时没有。',
    '更高音质档位依赖官方手机客户端，本客户端只提供标准 / HQ / SQ 三档。',
    '仅 x64，暂不支持 ARM64。',
    '不打包、不分发官方客户端的任何二进制；音源授权完全由服务端接口裁决。',
  ],
  privacy:
    '数据只在本机 %LOCALAPPDATA%\\Bowen，凭据用 Windows DPAPI 加密；无遥测，日志只写本地文件。',
  disclaimer:
    '本项目是个人学习用途的非官方第三方客户端，与波点音乐官方无任何关联，未获其授权或认可。请通过官方渠道支持正版。',
};
