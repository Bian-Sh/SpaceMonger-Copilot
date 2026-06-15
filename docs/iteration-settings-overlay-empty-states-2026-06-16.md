# 迭代纪要：设置全屏覆盖与空态统一（2026-06-16）

## 本次需求

- 继续按上一轮 UI 建议优化空态体验。
- 设置页面打开时应遮住整个 app，不再暴露顶部导航和底部状态栏。
- 设置按钮移动到窗口右侧，靠近系统关闭按钮左侧位置。

## 实现摘要

- `src/SpaceMonger.App/MainWindow.xaml`
  - 将窗口内容从单层 `DockPanel` 调整为根 `Grid + DockPanel + SettingsPage overlay`。
  - `SettingsPage` 从 `ContentArea` 内部移到窗口根 `Grid`，`Panel.ZIndex=100`，现在覆盖 toolbar、内容区和 status bar。
  - 顶部 toolbar 外层改为两列 `Grid`，左侧保留导航工具栏，右侧独立放置设置按钮。
  - 设置按钮右对齐，使用 `Margin="0,4,34,4"`，视觉上靠近窗口关闭按钮左侧。

- `src/SpaceMonger.App/Views/RecommendationsPanel.xaml`
  - 推荐清理空态背景从淡红错误底改为中性 `#FAFAFA`。
  - 无推荐项时拆成标题和说明两行，降低误报/错误感。
  - 真正的 `AnalysisError` 仍保留红色错误文本。

- `src/SpaceMonger.App/Views/ChatPanel.xaml`
  - 右侧聊天面板新增空态提示，避免大面积空白。
  - 空态提示说明扫描后可以询问磁盘占用、清理建议或选中文件夹解释风险。

- `src/SpaceMonger.App/ViewModels/ChatViewModel.cs`
  - 新增 `HasMessages` 状态。
  - 监听 `Messages.CollectionChanged`，用于控制聊天空态显隐。

## 验证

```powershell
dotnet build .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Debug --no-restore
```

- 构建通过。
- 仍存在既有 `NU1701` 包兼容警告，涉及 `OpenTK 3.3.1`、`OpenTK.GLWpfControl 3.3.0`、`SkiaSharp.Views.WPF 3.119.2`；本次未处理。
