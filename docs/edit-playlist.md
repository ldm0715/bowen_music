# 歌单详情：编辑自己的歌单

2026-10-03 实现。协议结论见
[`bodian-api-reference.md`](bodian-api-reference.md) §2.4 与
[`../reverse/findings/11-share-playlist-crud.md`](../reverse/findings/11-share-playlist-crud.md) §2.3。

歌单详情页右上角的「更多」菜单里，「编辑」此前只是一个占位 —— 点了弹一句「编辑歌单还没做」
（`PlaylistDetailViewModel.NotifyEditUnavailable`）。这一轮把它做通：**改名称、简介、封面、标签**。

**隐私改不了，而且是刻意的。** 见 §3.1。

---

## 1. 接口

### 1.1 编辑：`PUT service/playlist`

```
PUT service/playlist   signed
    Body: {"id": <int64>, "name": …, "description": …, "pic": …, "categoryList": [<int>, …]}
```

与新建（POST）、删除（DELETE）**共用同一条裸路径**，靠 HTTP method 区分。
键名全部来自反汇编字面量证据（`edit_user_playlist.dart:3796-3914`）。

**2026-10-03 实测走通**（桌面 `win` 头 + 桌面签名，客户端界面操作）：

```
编辑歌单请求已受理：歌单 100170429，名字 gogo，标签 3 个，
接口 service/playlist，业务码 200
```

反过来说明五件事都是对的：`id` 键名、PUT 动词、五个字段的拼法、`categoryList` 传 id 数组、
桌面签名对 PUT 一样有效。**`private` 不在 body 里**这条仍未实测否定过
（见 §3.1，静态证据足够，没去试）。

| 项 | 结论 | 来源 |
| --- | --- | --- |
| `id` 键 | 有字面量证据（`0x1440b0c`）。**旧记录里「按数组槽序推断」的说法已作废** | 反汇编 |
| `pic` | **原始字符串**，不是规范化过的 URL。不改封面时必须回传原值 | 推断（保守） |
| `categoryList` | 分类 **id 数组**（int），id 来自 `service/category/list` | 反汇编 |
| 上限 | 官方标签最多 3 个（「最多选择三个标签」），服务端是否强校验未知 | 官方文案 |
| **`private` / `isPrivate`** | **请求体里没有这个键** | 反汇编 |
| 回执 | 官方只看业务码（非 200 弹「歌单编辑失败」），`data` 不被使用 | 反汇编 |

### 1.2 封面：`POST service/playlist/uploadPic/{id}`

```
POST service/playlist/uploadPic/{playlistId}   signed
    multipart/form-data，字段名 file，内容是图片字节
```

- 歌单 id 在**路径上**，与 `service/playlist/info/{id}` 同形。
- **上传本身不换封面** —— 要把回执里的地址填进 `PUT` 的 `pic` 才算改完，是两步。
- **2026-10-03 实测走通**：界面选图 → 裁剪 → 保存，封面确实换掉了（multipart、字段名 `file`、
  query-only 签名都对）。
- 回执里封面 URL 的**具体键名仍未单独确认**（反汇编只知道它经 `fromImgJson` 解析出一个 URL），
  所以 `BodianApi.ExtractImageUrl` 依旧按候选键（`imgUrl` / `pic` / `url` …）逐个试，
  并兼容 `data` 直接是字符串的形态 —— 反正「取不到地址就抛」这条兜底在，不会静默填个空值。

### 1.3 客户端裁剪参数（来自官方客户端）

官方选图后**先裁剪再上传**（`edit_user_playlist.dart:2645-2659` 构造 `UploadUserInfoImage`）：

| 参数 | 值 | 含义 |
| --- | --- | --- |
| `maxWidth` / `maxHeight` | `1400` | 输出最长边上限（`getPhotoOrCameraImage` 的默认值是 2560） |
| `chipRatio` | `1.25` | 裁剪框宽高比。同一参数也用于 `croper_image_widget.dart`，即裁剪比例 |

服务端自己的体积/像素上限**未知**，静态解不出来。客户端主动压到 ≤1400、JPEG 质量 0.9。

### 1.4 读回：`service/playlist/info/{id}` 的 `categories`

**编辑界面的初值靠它。** 2026-10-03 只读实测（自己的歌单）：

```json
"categories": []          // 没有标签时是空数组
```

- 元素是**对象** `{id, name}`（官方模型 `CategoryItem { int? id, String? name }`，
  `discovery_tabbar_titles.dart`），不是裸 id。
- **只有 `info` 会给** —— `userCreate` 列表与收藏列表响应里都没有这个键。
  所以标签的初值只能等详情回来才有；只读首屏那会儿是空的。
- 键名是 `categories`（读）而**不是** `categoryList`（写）。两个名字不一样，
  与 `private` / `isPrivate` 是同一类陷阱。

---

## 2. 实现分层

### Core（`src/Bodian.Core/`）

| 文件 | 改动 |
| --- | --- |
| `Api/BodianRequest.cs` | `BodianHttpVerb` 加 `Put`；新增 `File` 与 `BodianFormFile` |
| `Api/BodianHttpTransport.cs` | `Put => HttpMethod.Put`；有 `File` 时发 `MultipartFormDataContent` |
| `Api/BodianHeaders.cs` | **ContentType 守卫**：只在 ContentType 还空着时才补 `application/json` |
| `Api/Dto/Requests/UpdatePlaylistBody.cs` | 新增，五个键 |
| `Api/Dto/PlaylistDto.cs` | 新增 `Categories` + `PlaylistCategoryDto` |
| `Models/Playlist.cs` | 新增 `CoverRawUrl`（原始 pic 串）与 `Categories` |
| `Api/Endpoints.cs` | 新增 `PlaylistUploadPic(id)` |
| `Api/BodianApi.cs` | `UpdatePlaylistAsync` / `UploadPlaylistCoverAsync` + `ExtractImageUrl` |
| `Media/ImageViewportState.cs` | 新增 `CoverMode`（cover 语义）与 `SourceRect()` |

### UI（`src/Bodian.WinUI/`）

| 文件 | 改动 |
| --- | --- |
| `Controls/AppDialogs.cs` | 从 `MainWindow.CreateAppDialog` 提出来的共享 helper |
| `Controls/EditPlaylistDialogContent.xaml(.cs)` | 编辑表单 + 内嵌裁剪器 |
| `Controls/ImageCropView.xaml(.cs)` | 1.25:1 裁剪器 |
| `Controls/WrapPanel.cs` | 按内容定宽、放不下就换行的面板（标签用） |
| `Controls/TagChip.cs` | 标签 chip 的可选状态 |
| `ViewModels/PlaylistDetailViewModel.cs` | 编辑态、`LoadCategoriesAsync`、`SaveEditAsync`、就地刷新头部 |
| `Views/PlaylistDetailPage.xaml.cs` | `OnEditPlaylistClick` → 对话框 |
| `Services/IPlaylistLibrarySink.cs` | 新增 `OnPlaylistUpdated` |
| `ViewModels/SidebarViewModel.cs` | `UpdatePlaylist`：侧栏那一行就地换名字与封面 |
| `Services/IWindowHandleProvider.cs` | 新增：桌面端选文件要绑定宿主窗口 |

---

## 3. 几个决定的理由

### 3.1 不做隐私开关

两条独立证据说明**官方客户端创建之后就改不了隐私**：

1. `PUT` 的请求体里没有 `private` / `isPrivate` 键；
2. 「设置为隐私歌单」这句文案在整个解包产物里**只被创建对话框
   （`widget_create_play_list.dart`）引用**，编辑页没有任何隐私文案。

所以编辑界面不摆这个开关。摆一个「看着能改、实际被服务端忽略」的开关，
比不摆更糟 —— 用户以为改了，实际没改。真要改只能**删了重建**，
代价是丢掉 id、收藏与播放数。

> 服务端是否愿意接受 PUT 上多带一个 `private` 字段，静态看不出（未实测）。
> 若将来实测发现能改，再加开关。

### 3.2 `pic` 回传原始串而不是 `Uri`

`Playlist.CoverImage` 是 `ToHttpUri` **规范化过**的产物。回传改写过的地址是否被接受
没有验证过，所以另存一份原始 pic 串（`Playlist.CoverRawUrl`），PUT 时回传它。
这是唯一能保证「原样回传」的来源。

### 3.3 编辑用 `ContentDialog` 而不是独立页面

编辑入口就在详情页的「更多」菜单里。歌单详情是从侧栏**换根**进来的根页，
再压一个子页面进去，返回栈无处安放 —— 与删除、取消收藏同一条规矩，
需要确认/输入的都是弹层，且弹层在页面里构造（ViewModel 拿不到 `XamlRoot`）。

### 3.4 裁剪器内嵌在同一个对话框里

同一个 `XamlRoot` 上**同时只允许一个 `ContentDialog`**，选完图再弹一个「裁剪」对话框会直接抛。
所以是把这个对话框的内容切成裁剪器（表单/裁剪两块互斥显示），而不是叠一层。

裁剪期间「保存」置灰 —— 那会儿点它没有意义。内容是靠 `StateChanged` 事件让页面重算的。

### 3.5 出图走 Win2D，不走 `RenderTargetBitmap` / `BitmapTransform.Bounds`

- `RenderTargetBitmap` 只能拿到**显示分辨率**的像素，图放大过后裁出来是糊的。
- `BitmapTransform.Bounds` 与 EXIF 方向的先后关系不好确定，容易出「预览是正的、裁出来是歪的」。
- Win2D 的 `DrawImage` 源矩形作用在**已经摆正的** `SoftwareBitmap` 上，不存在这层歧义，
  顺带把「缩到 ≤1400」一起做了（`HighQualityCubic`）。

EXIF 在解码时就一次性摆正（`RespectExifOrientation`），预览与出图用同一张位图，
坐标天然一致。

**预览显示走 `BitmapImage`，不走 `SoftwareBitmapSource`**，缩放平移走普通的
`RenderTransform`（`ScaleTransform` + `TranslateTransform`）而不是
`ElementCompositionPreview` 那套合成器属性：

- `SoftwareBitmapSource` 在本项目里**只有裁剪器用过**，别处一律 `BitmapImage`
  （封面、二维码、评论看图）。它出问题的方式是**静默不显示**，很难查。
  代价是把摆正后的位图再编码一次喂给 `BitmapImage` —— 只影响预览，出图用的仍是原始位图。
- 合成器属性是给拖动时的平滑用的，裁剪器没有那个需求；用 `RenderTransform` 更直白，
  坐标与视口状态一一对应。

**alpha 模式两头不一样，不能统一**：

| 环节 | 要的 alpha | 原因 |
| --- | --- | --- |
| 解码（出图用） | **`Premultiplied`** | Win2D 的 `CanvasBitmap.CreateFromSoftwareBitmap` 只收 Premultiplied 的 Bgra8，换成 `Ignore` 会让「完成裁剪」抛 `COMException`（实测踩过，见下） |
| 编码预览（显示用） | **`Ignore`** | JPEG 编码器不收带 alpha 的位图；喂进去拿到的是坏流，而 `BitmapImage` 拿到坏流**不抛异常**，只是什么都不画 |

所以解码出来是 Premultiplied，**预览前用 `SoftwareBitmap.Convert` 单独转一份无 alpha 的**。

`Image.ImageFailed` 必须接，且出图那一步的异常必须当场接住：`OnCropDoneClick` 是
`async void` 的点击处理器，异常跑出去就是整个应用闪退 —— Win2D 那个 `COMException`
就是这么崩的。

### 3.6 标签初值读 `info` 的 `categories`

`Playlist.Categories` 复用 `MusicCategory` —— 标签**候选项**（`service/category/list`
的子分类）与**已选项**是同一套 id，同一种东西。界面因此不用维护两套模型。

`CoverMode` 与 `SourceRect()` 加在既有的 `ImageViewportState` 上，而不是另写一套：
那个状态机已经在评论看图里跑着，且是 Core 层可单测的。`CoverMode` 是个显式开关
（默认关）—— 图片查看器要的是 contain（整张图看得见），裁剪器要的是 cover（铺满、可裁），
把默认改掉会连带弄坏看图。

### 3.7 标签用自写的 `WrapPanel`，不用 `GridView` / `ItemsWrapGrid`

`ItemsWrapGrid`（以及 `VariableSizedWrapGrid`、`UniformGridLayout`）给一屏里的**所有条目
用同一个格子尺寸**，由第一个条目定下来 —— `ArtistDetailPage` 的注释里记过这个坑。
换成标签之后就是：第一个 chip 是「网红」（两个字），后面「中国风古风」这种长词全被裁掉。

所以写了一个 `WrapPanel`：每个子项按自己的内容定宽、放不下就换行。
46 个标签的数据量不需要虚拟化，`ItemsControl` 就够。

**上限兜底要直接改 `ToggleButton.IsChecked`，不能只改模型。** chip 的选中态是
`IsChecked="{x:Bind IsSelected, Mode=OneWay}"`；点第 4 个时 `ToggleButton` 自己已经把
视觉状态切过去了，而模型上的 `IsSelected` 本来就是 `false` —— 赋同样的值**不发通知**，
OneWay 绑定也就不会把视觉状态收回去。结果是那个 chip 一直显示为选中，却没被算进选中集合，
界面与状态对不上（用户看到「提示最多 3 个，可保存照样发出去」）。

### 3.8 裁剪视口的尺寸是**定死的常量**

视口固定 350×280（= 1.25:1），**不由 code-behind 去量外层容器再赋回来**。

按容器量的写法有两个连环坑，踩中任一都会让这个 Grid 变成 0×0：

1. 外层是 `Visibility` 切出来的，**隐藏时量不到尺寸**（`ActualWidth` 为 0）；
   而外层自身的高度在「表单 → 裁剪」切换前后未必变化，`SizeChanged` 也就未必来。
2. 在 `SizeChanged` 回调里改 `Width`/`Height`，WinUI 会把这一改**并回当前那轮布局**，
   刚写进去的值不生效。

而 **0 尺寸的子树在渲染时会被整个丢掉** —— 表现是「边框和图片一起不见」，
偏偏裁剪数学照常成立（`SourceRect()` 只看状态机，不看元素尺寸），
所以「点完成裁剪能出图，但屏幕上什么都没有」。这个组合极难从现象倒推，实测卡了三轮。

定死之后这些坑一次性都没有了：容器再小也只是居中溢出，不会消失。
视口小一点不影响出图分辨率 —— 输出尺寸是 `min(裁剪覆盖的源像素, 1400)`，与视口 DIP 大小无关。

配套的一条：`EditPlaylistDialogContent` 仍要**先切到裁剪视图再解码**，让解码时视口已经是可见的。

### 3.9 multipart 的签名不含二进制 body

桌面签名对 body 算的是 `md5(body + "kuwotest")`，那是针对 **JSON 字符串**的；
二进制没有良定义的字符串形态。所以上传请求的签名只覆盖 path 与 query。
**这一条未实测**，代码里注明了；风险窗口很小 —— `ver ≤ 3.0.0` 服务端根本不校验签名。

顺带修掉一处会砸掉 multipart 的缺陷：`BodianHeaders.Apply` 原来只要 `Content` 非空
就无条件把 `ContentType` 设成 `application/json`，那会**覆盖掉 `MultipartFormDataContent`
自带的 boundary**，服务端就解析不出文件。现在只在 ContentType 还空着时才补。

---

## 4. 验证

### 离线

```bash
dotnet test --project tests/Bodian.Core.Tests/Bodian.Core.Tests.csproj
```

覆盖：PUT 的 method / 裸路径 / 五个键、请求体**不含隐私键**、名字 trim 与空白拦截、
未登录与换账号的作废、multipart 的字段名与 Content-Type 保留、回执缺 URL 时抛错；
`CoverMode` 的铺满与放大、`SourceRect()` 的换算与边界钳制。

### 仍未验证的部分

- **空 `categoryList` 会不会清掉已有标签** —— 详情能读回标签（`categories`）并且界面会原样回传，
  所以正常路径不会踩到；但服务端对空数组的解释没测过
- `uploadPic` 回执里封面 URL 的**具体键名**（结果是对的，只是没去逐字核对是哪个键）
- 服务端对图片体积/像素/格式的限制
- **隐私不能改**是静态推断的结论（body 里没有 `private` 键 + 官方编辑页没有开关），
  没有实测否定过

### 界面验收清单（手动）

| 项 | 怎么看 |
| --- | --- |
| 初值 | 打开编辑对话框，标题 / 简介 / 封面 / 已选标签都该是歌单当前的样子 |
| 深色模式 | 切到深色再打开，对话框不该是白板，内容与按钮之间不该有大片留白 |
| 空名 | 把标题清空，「保存」应变灰 |
| 标签上限 | 选到第 4 个应被弹回去，并提示「最多选择 3 个标签」 |
| 裁剪 | 选一张图 → 视口是 1.25:1、铺满不留白 → 拖动/滚轮有效 → 「完成裁剪」后预览更新 |
| 出图 | 裁剪保存后，重启客户端再从服务端拉，封面应是裁过的那张（不是原图，也不是糊的） |
| 侧栏同步 | 改名/换封面后，侧栏那一行的名字与封面应立刻跟着变 |
| 隐私 | 编辑对话框里**没有**隐私开关 |
