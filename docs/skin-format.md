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
| `preview` | 否 | 预览图路径；资源不存在时会被忽略。 |
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
{ "type": "settings", "topmost": true, "visible": true, "maximized": false, "language": "zh-CN" }
{ "type": "collection-error", "messageKey": "collectionFailed" }
```

皮肤向宿主发送：

```json
{ "type": "toggle-topmost" }
{ "type": "hide-window" }
{ "type": "toggle-maximize" }
{ "type": "close-window" }
{ "type": "begin-drag" }
{ "type": "set-language", "language": "en-US" }
```

## 皮肤管理界面协议

内置 HUD 的皮肤管理弹窗通过以下消息读取、切换、导入和导出皮肤；外部皮肤也可以复用同一组消息。

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
{ "type": "request-skins" }
{ "type": "set-skin", "id": "my-skin" }
{ "type": "import-skin" }
{ "type": "export-skin", "id": "my-skin" }
{ "type": "export-skill" }
```

- `request-skins`：请求宿主重新扫描安装目录并返回 `skin-catalog`。
- `set-skin`：切换皮肤；宿主会写入设置并重新加载页面。失败时通过 `skin-action` 返回错误。
- `import-skin`：由宿主打开 Windows 原生文件选择框，导入 `.boostskin`；弹窗导入只导入，不自动切换，刷新目录后由用户点击缩略图切换。托盘菜单导入仍会自动切换。
- `export-skin`：由宿主打开保存对话框，复制当前已安装的外部包；内置皮肤返回失败。
- `export-skill`：由宿主打开保存对话框，导出包含本机完整传感器清单与最新快照的皮肤制作 Skill ZIP；用于让 Agent 根据本机能力生成 `.boostskin`。

宿主处理完操作后发送：

```json
{ "type": "skin-action", "action": "import", "ok": true, "message": "皮肤已导入。" }
```

`action` 为 `select`、`import`、`export` 或 `export-skill`；`ok` 为 `false` 时，页面展示本地化的失败提示。

## 安装与回退

- 安装目录：`%LocalAppData%\TheBoostDeck\Skins`。
- 导入包复制为 `<id>-<SHA256前8位>.boostskin`；同一 id 的多个包按修改时间取最新。
- 解压缓存位于 `Skins\.cache`，按包 SHA256 复用。
- 包大小上限 256 MB，最多 2000 个条目，单条目解压上限 64 MB，总解压上限 512 MB。
- 拒绝绝对路径、`..` 路径、盘符和查询串；入口必须存在。
- 皮肤损坏、缺失或版本不兼容时回到 `classic`，不影响主程序启动；导航失败会尝试恢复内置皮肤。

## 设计边界

皮肤负责横屏 16:9、竖屏 9:16 以及窗口缩放下的布局。宿主只提供 WebView2 画布、遥测消息和窗口控制，不嵌入皮肤的 HTML/CSS/JS 到主程序代码中。Skill 只需生成符合上述格式的 ZIP 包即可。

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
│   ├── platform.json
│   └── platform-summary.md
└── scripts/
    └── validate_boostskin.py
```

- `platform.json` 在导出时写入：机器名、OS、架构、应用版本、传感器驱动状态、最新 `TelemetrySnapshot`、全部原始硬件传感器条目、规范化指标覆盖摘要，以及 `derivedMetrics` 二次计算候选。
- `derivedMetrics` 列出可由 `snapshot` 直接字段安全计算的候选，例如内存/显存剩余、每块磁盘剩余空间、磁盘总剩余空间、网络上下行合计、CPU + GPU 功耗；每项包含 `formula`、`inputs`、`available`、`value`、`unit` 与说明。这些值由皮肤在每次 `snapshot` 到达时重新计算，宿主不会直接下发。
- `platform-summary.md` 是同一份平台清单的可读版本，按硬件列出全部传感器，并包含规范化指标覆盖和派生指标候选表。
- 原始传感器清单用于能力发现；当前皮肤运行时只接收 `snapshot` 中的规范化字段。未映射进 `snapshot` 的原始传感器不能直接作为实时 UI 数据源，Skill 文档已明确这一边界，并说明派生值也只能基于 `snapshot` 中可用的直接字段。
- 导出的是 Skill 文件夹的 ZIP，不是 `.boostskin`；解压后可由 Agent 作为本地 Skill 使用。
