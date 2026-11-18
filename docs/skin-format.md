# TheBoostDeck 皮肤包格式

皮肤包是扩展名为 `.boostskin` 的 ZIP 压缩包。它只包含 UI 显示层需要的文件，主程序负责发现、校验、安装、加载皮肤，并继续提供遥测快照和窗口控制通道。

## 最小结构

```text
my-skin.boostskin
├── skin.json
├── index.html
├── assets/
│   ├── logo.png
│   └── font.woff2
└── styles/
    └── skin.css
```

`skin.json` 示例：

```json
{
  "schemaVersion": 1,
  "id": "my-skin",
  "name": "My Skin",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "A custom HUD for TheBoostDeck.",
  "entry": "index.html",
  "preview": "preview.png",
  "window": {
    "width": 1440,
    "height": 900,
    "minWidth": 800,
    "minHeight": 450,
    "background": "#040A15"
  }
}
```

## 字段

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `schemaVersion` | 否 | 当前只支持 `1`，默认 `1`。 |
| `id` | 否 | 皮肤唯一标识；缺省时从包文件名生成安全 id。只允许字母、数字、点、横线和下划线，长度 1–80；`classic` 保留给内置皮肤。 |
| `name` | 否 | 托盘菜单显示的名称；缺省时使用 id。 |
| `version` | 否 | 皮肤版本，默认 `1.0.0`。 |
| `author` | 否 | 作者信息，仅作元数据。 |
| `description` | 否 | 皮肤说明，仅作元数据。 |
| `entry` | 否 | 入口 HTML，默认 `index.html`；必须存在于包内，且入口资源应使用相对路径。 |
| `preview` | 否（作者 Skill 导出的皮肤必须提供） | 预览图路径；资源不存在时会被忽略，皮肤库里显示占位图。 |
| `window.background` | 否 | 宿主窗口底色，使用 WPF 可解析的颜色字符串；用于皮肤加载前的过渡底色。 |
| `window.width` / `height` / `minWidth` / `minHeight` | 否 | 预留给未来窗口适配，当前宿主不据此强制改变窗口尺寸；皮肤应自行处理响应式布局。 |

## 资源与 URL

加载外部皮肤时，宿主把皮肤解压目录映射为：

```text
https://skin.theboostdeck/<entry>
```

皮肤内的图片、字体、CSS、JS 应使用相对路径，例如：

```html
<link rel="stylesheet" href="styles/skin.css">
<img src="assets/logo.png">
```

旧的兼容主题仍映射在 `https://themes.theboostdeck/custom.css`，内置经典 HUD 继续使用 `classic` 运行时。外部皮肤不能把主页面导航到 `skin.theboostdeck` 之外；新窗口请求也会被阻止。

## 宿主消息协议

宿主向皮肤页面发送的消息：

```json
{ "type": "snapshot", "data": { } }
{ "type": "settings", "topmost": true, "visible": true, "maximized": false, "language": "zh-CN", "refreshIntervalMs": 1000 }
{ "type": "collection-error", "messageKey": "collectionFailed" }
```

`refreshIntervalMs` 是当前遥测采样间隔；皮肤可用它来显示节奏提示，但不要据此自行启动另一套轮询。

皮肤向宿主发送：

```json
{ "type": "toggle-topmost" }
{ "type": "hide-window" }
{ "type": "toggle-maximize" }
{ "type": "close-window" }
{ "type": "begin-drag" }
{ "type": "set-language", "language": "en-US" }
```

## 宿主控制中心协议

内置 HUD 的宿主控制中心通过以下消息读取设置、切换皮肤、导入和导出皮肤；外部皮肤也可以复用同一组消息。

宿主向皮肤页面发送：

```json
{
  "type": "skin-catalog",
  "activeId": "classic",
  "skins": [
    {
      "id": "classic",
      "name": "经典 HUD",
      "version": "built-in",
      "author": "TheBoostDeck",
      "description": "内置的自适应性能 HUD。",
      "isBuiltIn": true,
      "canExport": false,
      "previewUrl": "data:image/png;base64,..."
    }
  ]
}
```

- `previewUrl` 从 `skin.json` 的 `preview` 字段读取，转换为 `data:` URL 后下发；没有预览图或图片无法读取时为 `null`，页面应显示占位图。
- 内置 `classic` 的预览使用内置 `logo.png`；内置皮肤不可导出。
- 安装目录中的外部皮肤按包路径读取预览图；预览图只用于管理界面缩略图，不会改变皮肤入口页面。

皮肤向宿主发送：

```json
{ "type": "open-settings" }
```

- `open-settings`：打开宿主统一控制中心（默认为“通用”页，另有皮肤和关于页签）。皮肤自身不渲染设置、目录、预览卡片、导入/导出或切换逻辑。
- `open-skin-manager` 可选：直接打开同一控制中心的“皮肤”页签，适合在界面空间允许时提供一键切换入口。
- 宿主控制中心内部使用 `request-settings`、`set-refresh-interval`、`set-topmost`、`set-startup`、`request-skins`、`set-skin`、`import-skin`、`export-skin`、`export-skill` 与 `skin-catalog` / `skin-action` / `settings` 完成操作；这些消息属于宿主层，不作为外部皮肤的 UI 协议。旧版皮肤若仍内置复制的设置/管理面板，不再保证可用，建议用新 Skill 重新生成。

## 安装与回退

- 安装目录：`%LocalAppData%\TheBoostDeck\Skins`。
- 导入包复制为 `<id>-<SHA256前8位>.boostskin`；同一 id 的多个包按修改时间取最新。
- 解压缓存位于 `Skins\.cache`，按包 SHA256 复用。
- 包大小上限 256 MB，最多 2000 个条目，单条目解压上限 64 MB，总解压上限 512 MB。
- 拒绝绝对路径、`..` 路径、盘符和查询串；入口必须存在。
- 皮肤损坏、缺失或版本不兼容时回到 `classic`，不影响主程序启动；导航失败会尝试恢复内置皮肤。

## 删除皮肤

皮肤库每张预览卡左上角有删除按钮，点击后由 WebView 层的确认弹窗二次确认。

- 删除某个皮肤会移除该 id 在 `Skins` 目录下的全部 `.boostskin` 包（同一 id 的多个历史版本一并清理），并删除 `.cache` 中对应的解压目录。
- 删除正在使用的皮肤时，宿主先切回内置皮肤再删除文件；由于皮肤切换会重新导航，控制中心会在新皮肤加载完成后自动重新打开“皮肤”页签。
- 内置皮肤也可以删除：它内置于程序，删除只是把它从皮肤列表移除，并继续作为加载失败时的回落皮肤。列表里至少需要保留一个已安装的外部皮肤才能移除内置皮肤。
- 移除内置皮肤后，页脚会出现“恢复内置皮肤”按钮，可随时把条目加回列表。该状态记录在 `settings.json` 的 `hideBuiltInSkin` 字段。
- 删除失败通常是因为包文件被其他程序占用，关闭占用后重试即可。

## 设计边界

皮肤负责横屏 16:9、竖屏 9:16 以及窗口缩放下的布局。宿主只提供 WebView2 画布、遥测消息和窗口控制，不嵌入皮肤的 HTML/CSS/JS 到主程序代码中。Skill 只需生成符合上述格式的 ZIP 包即可。

### 宿主保留的边缘命中区

窗口无边框、WebView2 铺满整个客户区，因此宿主要在每次页面导航完成后注入一圈透明的缩放入口：四条边各 6 CSS px、四个角各 12 CSS px。它不改变任何视觉，只在该区域内接管指针，用于改变窗口大小。

- 交互控件（按钮、链接、输入框、下拉、可滚动列表、拖拽握把）距窗口边缘至少 16 px，并避开四角；不要把控件唯一可点的面积做成贴边的细条。
- 背景、边框、光晕和装饰图形可以继续铺到边缘——命中带是透明的，只截获指针输入，不影响视觉。
- 顶部 6 px 属于缩放，拖拽区从其内侧开始；整条标题栏做拖拽面仍然可行。
- 皮肤不要自行实现缩放握把、缩放消息，也不要用超大 `z-index` 的全屏覆盖层；宿主命中带位于 CSS `z-index` 上限，覆盖它会直接导致无法缩放。
- 窗口最大化时宿主自动隐藏命中带，皮肤无需关心该状态。

## 平台专属 Skill 导出

皮肤管理界面的“导出 Skill”会生成一个 ZIP，顶层目录为 `theboostdeck-skin-author/`：

```text
theboostdeck-skin-author/
├── SKILL.md
├── references/
│   ├── skin-format.md
│   ├── protocol.md
│   ├── design-guide.md
│   ├── derived-metrics.md
│   ├── from-reference.md
│   ├── styles.md
│   ├── platform.json
│   └── platform-summary.md
└── scripts/
    ├── validate_boostskin.py
    └── render_preview.py
```

- 设计路径分三条：给了参考图走 `from-reference.md`（图面清单 → 指标映射 → 冲突裁决 → 双比例推导）；给了风格词走 `styles.md`（内置 16 套完整风格，含二次元、赛博朋克、可爱风）；两者都没有时先反问一次，未答则按 `styles.md` 的轮换顺序兜底，并明确说明选了哪一套。
- `from-reference.md` 记录了“图多机少”的补位顺序：先用二次计算值（CPU + GPU 功耗、总存储容量、总存储占用、总剩余存储、内存/显存剩余、上下行合计），再拆分单个指标，再合并小模块，最后才按信息层级裁剪。
- `styles.md` 内置 16 套风格档案，每套给出背景、面板、强调色、警示色、文字色、字体层级、圆角与描边、质感、图标语言、模块处理方式和适用场景。
- `render_preview.py` 用本机 Chromium/Edge 无头模式渲染 `?preview=1` 的皮肤并输出 PNG；预览图现在是包的必需内容，校验脚本会拒绝缺少 `preview` 的包。
- `validate_boostskin.py` 除结构校验外，还会检查 `snapshot.<字段>` 是否存在于本机 `platform.json`、必需的按钮能力（设置/换肤入口、`toggle-maximize`、最小化/关闭、`begin-drag`）是否齐全、是否引用远程资源、是否存在超过宿主命中带层级的 `z-index`，并对“有最大化按钮却不读 `maximized` 状态”和“未处理 `?preview=1`”的皮肤给出警告。

- 宿主控制中心由宿主提供，默认打开“通用”页：皮肤只保留一个可见入口，点击后发送 `open-settings`。宿主负责皮肤目录、预览缩略图、切换、导入、导出、导出 Skill、通用设置（语言、刷新频率、始终置顶、开机自启）、Esc/遮罩关闭和中英文字段；皮肤包不再复制设置或管理面板。这样所有皮肤共用一套面板，版本升级和维护不会分叉。
- `platform.json` 在导出时写入：机器名、OS、架构、应用版本、传感器驱动状态、最新 `TelemetrySnapshot`、全部原始硬件传感器条目、规范化指标覆盖摘要，以及 `derivedMetrics` 二次计算候选。
- `derivedMetrics` 列出可由 `snapshot` 直接字段安全计算的候选，例如内存/显存剩余、每块磁盘剩余空间、磁盘总剩余空间、磁盘总容量、磁盘总占用、网络上下行合计、CPU + GPU 功耗；每项包含 `formula`、`inputs`、`available`、`value`、`unit` 与说明。这些值由皮肤在每次 `snapshot` 到达时重新计算，宿主不会直接下发。
- `platform-summary.md` 是同一份平台清单的可读版本，按硬件列出全部传感器，并包含规范化指标覆盖和派生指标候选表。
- Skill 的布局规则要求：只为本机标记为 `available` 的指标设计组件；本机 `unavailable` 的指标和整组硬件直接不纳入布局，不保留空卡片、空行或 “N/A” 占位。原始传感器清单用于能力发现；当前皮肤运行时只接收 `snapshot` 中的规范化字段，未映射进 `snapshot` 的原始传感器不能直接作为实时 UI 数据源。派生值也只能基于 `snapshot` 中可用的直接字段，输入不可用时同样不显示。
- 每个皮肤必须包含这组按钮能力：设置与换肤至少有一个（`open-settings` 或 `open-skin-manager`）；最大化/复原按钮必须存在（发送 `toggle-maximize`，并根据 `settings.maximized` 切换图标与提示）；最小化与关闭至少有一个（`hide-window` 即宿主的收起动作，`close-window` 为退出）；另需一个可见的拖拽区域发送 `begin-drag`。建议把整条标题栏作为拖拽面，并把按钮、链接、输入框等交互控件排除在拖拽命中区之外；这只是建议，不是固定版式，拖拽区也可以是一条较窄的横条、边缘握把或专用手柄，只要在横竖屏下都可见可达即可。设置与换肤入口可以和收起/退出合并成一个控制菜单，位置不固定，但必须在横屏和竖屏下可见可达。宿主统一显示控制中心；旧版皮肤如果仍自带复制的面板，建议用新 Skill 重新生成，不再维护本地设置/管理 UI。
- 导出的是 Skill 文件夹的 ZIP，不是 `.boostskin`；解压后可由 Agent 作为本地 Skill 使用。
