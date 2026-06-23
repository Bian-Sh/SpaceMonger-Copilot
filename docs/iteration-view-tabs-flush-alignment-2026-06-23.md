# 顶部视图 Tab 贴边对齐调整（2026-06-23）

## 背景

- 顶部“树状图 / 树形列表 / 关于”Tab 与其背景容器之间存在内边距。
- 需求是与下方“推荐清理 / 控制台”Tab 风格保持一致：左侧第一个 Tab 的左边与整条 Tab 背景左边重合，所有 Tab 顶部与 Tab 背景顶部重合。

## 变更

- 修改 `src/SpaceMonger.App/MainWindow.xaml`。
- 移除顶部视图 Tab 外层 `Border` 的 `Padding="4,2"`。
- 上下两组 Tab 继续共用 `VP.TabButton`，避免顶部和底部出现两套不同风格。
- 追加调整 `src/SpaceMonger.App/Themes/VisionProTheme.xaml`：将 `VP.TabButton` 顶部圆角从 `6,6,0,0` 统一到 `10,10,0,0`，与导航栏顶层 container 的圆角保持一致。
- 统一窗口级圆角到 `10`：`VP.GlassPanelLarge`、顶部/底部 Tab 背景、TabButton、推荐清理底部贴边区域、ChatPanel 顶部警告区和底部输入区都使用导航栏顶层 container 的半径。
- 统一上方“树状图 / 树形列表”窗口板块风格：Treemap 和 TreeView 内容底板改为与“推荐清理 / 控制台”一致的 `VP.SurfaceBrush`、`VP.BorderLightBrush`、`0.5` 边框和窗口阴影。
- TreeView Header 区域改为轻微区分的 `VP.SurfaceHoverBrush`，并增加底部分界线，使列名区域和列表内容有清晰层级。
- About 页也包入同一套窗口底板：`VP.SurfaceBrush`、`VP.BorderLightBrush`、`0.5` 边框、`10` 底部圆角和同款窗口阴影。
- About 页“已是最新版本”状态的对勾改为 XAML 固定图标，避免英文资源缺少对勾；中文资源移除内嵌 emoji，防止重复显示。
- 固定对勾改为主题控制的绿色图标：使用 `VP.SuccessBrush` 描边方框和绿色 `✓`，避免 emoji 在 WPF 字体回退中渲染为黑色。

## 验证

- 执行 Release build 与 `win-x64` self-contained folder publish 成功。
- 最新发布目录：`outputs/SpaceMonger-win-x64-folder-20260623-080422`。
- 启动发布后的 `SpaceMonger.App.exe` 成功，主窗口标题为 `SpaceMonger Next`。
- 截图目测确认：顶部“树状图”Tab 左侧和顶部已经与 Tab 背景贴齐，行为与下方“推荐清理 / 控制台”一致。
- 进程路径校验确认启动的是最新发布包 `outputs/SpaceMonger-win-x64-folder-20260623-080422/SpaceMonger.App.exe`。
- 本次截图通道受到 OpenAI Translator 浮层干扰，不作为最终验收依据；圆角验收以 XAML diff、构建发布和正确进程启动为准。

## 已知 warning

- 保留既有 `NU1701` 包兼容警告。
- 保留既有 `CS8604` nullable 警告。
- 保留既有 `Strings.resx` 重复资源名 `NoItemsSelectedForCleanupToolTip` 警告。

注意：本次发布是 folder publish，分发时需要复制整个目录，不要只复制单个 exe。
