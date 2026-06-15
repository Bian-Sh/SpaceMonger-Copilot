# 迭代纪要：空态与控制台筛选交互（2026-06-15）

## 本次需求

- 将 treemap 空态文案从“无数据”调整为“无数据可供展示，请扫描~”。
- 正在扫描时不再叠加显示空态文案，只保留扫描遮罩和进度。
- 控制台日志筛选改为 Unity Console 风格：在控制台 tab 右侧放置竖向省略号入口，通过 context menu 以 flags 复选框组合筛选日志级别。
- 不再使用“最低等级阈值”过滤方式，避免只想看 `Info + Error` 这类组合时无法表达。

## 实现摘要

- `src/SpaceMonger.App/Views/TreemapView.xaml`
  - 更新 `NoDataText` 文案。
- `src/SpaceMonger.App/Views/TreemapView.xaml.cs`
  - 新增 `_isScanning` 状态。
  - `SetScanningState` 中同步刷新空态。
  - `UpdateEmptyState` 在扫描中强制隐藏空态。
- `src/SpaceMonger.App/MainWindow.xaml`
  - 将 `ConsoleTab` header 改为自定义 header：左侧“控制台”，右侧 `⋮` 按钮。
  - `⋮` 按钮挂载 `ContextMenu`，包含 `Verbose`、`Info`、`Warning`、`Error` 四个可组合复选项，点击复选项后菜单保持打开，便于连续组合。
  - 移除控制台内容区域顶部占空间的 `Log Level` 菜单行。
- `src/SpaceMonger.App/MainWindow.xaml.cs`
  - 将 `_minimumConsoleLevel` 改为 `_visibleConsoleLevels` flags。
  - `RefreshConsoleText` 改为按 flags 包含关系过滤。
  - `ConsoleLogLevel` 标记 `[Flags]`，每个等级改为独立 bit。

## 截图观察到的可改善点

- 控制台筛选入口原本占用正文第一行空间，而且 “Log Level” 文案偏工程化；移动到 tab header 后更接近工具窗口语义。
- 空态“无数据”信息量不足，用户不知道下一步动作；新文案直接提示扫描。
- 扫描中同时显示“无数据”和扫描状态会让状态冲突；现在扫描态优先。
- 底部左右区域视觉权重差异较大，后续可以考虑让 chat 面板空态也给出明确提示，避免右侧大片空白。
- 推荐清理 panel 空态已有解释，但淡粉背景较抢眼；后续可考虑降低强调度或与控制台/treemap 空态统一视觉语言。

## 验证

```powershell
dotnet build .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Debug
```

- 构建通过。
- 仍存在既有 `NU1701` 兼容性警告，涉及 `OpenTK 3.3.1`、`OpenTK.GLWpfControl 3.3.0`、`SkiaSharp.Views.WPF 3.119.2`；本次未处理。
