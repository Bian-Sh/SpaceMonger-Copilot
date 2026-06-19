# 迭代纪要：分析/清理按钮布局调整 — 2026-06-19

## 需求
- 把"分析"按钮从工具栏挪到推荐面板底部 Summary Bar，"清理"按钮右侧
- 清理按钮与分析按钮并排（清理在左，分析在右）
- 清理按钮默认不可交互（`TotalSelectedCount == 0` 时 disabled）
- 禁用态 hover 显示 "未选中待清理项"，通过 i18n 机制走多语言，不硬编码

## 改动文件

### `src/SpaceMonger.App/MainWindow.xaml`
- 移除工具栏中的 `AnalyzeButton`（原 DockPanel.Dock="Right" 位置的注释 + 整个 Button 元素）

### `src/SpaceMonger.App/MainWindow.xaml.cs`
- 移除不再需要的 `AnalyzeButton_Click` 方法（该方法仅调用 `OnAnalyzeRequested()`，此调用已通过 RecommendationsPanel 的事件机制覆盖）

### `src/SpaceMonger.App/Views/RecommendationsPanel.xaml`
- Summary Bar (Row 2) 中：
  - 原单独的 `CleanUpButton` 替换为 `StackPanel Orientation="Horizontal"` 包裹两个按钮
  - 清理按钮：Style 含 DataTrigger，`TotalSelectedCount == 0` 时 `IsEnabled=False`，ToolTip 切换为 `NoItemsSelectedForCleanupToolTip`
  - 分析按钮：Style 含 DataTrigger，`IsAnalyzing == True` 时 `IsEnabled=False`，ToolTip 切换为 `AnalysisInProgressToolTip`
  - 均设置 `ToolTipService.ShowOnDisabled="True"` 确保禁用态 tooltip 可显示

### `src/SpaceMonger.App/Localization/Strings.resx`
- 新增 key `NoItemsSelectedForCleanupToolTip` = "No items selected for cleanup"

### `src/SpaceMonger.App/Localization/Strings.zh-CN.resx`
- 新增 key `NoItemsSelectedForCleanupToolTip` = "未选中待清理项"

## 关键技术点
- 使用 `System.IO.File.ReadAllText` / `WriteAllText` 显式 UTF-8 处理 resx，避免 PowerShell `Get-Content` 的编码损坏问题（历史教训）
- 清理按钮的 IsEnabled 直接绑定到 RecommendationsViewModel.TotalSelectedCount，通过 Style DataTrigger 控制
- 分析按钮的 IsEnabled 绑定到 RecommendationsViewModel.IsAnalyzing

## 打包
- `outputs\SpaceMonger-win-x64-folder-20260619-154411\SpaceMonger.App.exe`