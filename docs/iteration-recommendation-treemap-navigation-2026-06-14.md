# 迭代纪要：推荐清理路径回跳 Treemap（2026-06-14）

## 背景

“推荐清理”面板列出的路径来自当前扫描/分析结果，但此前点击路径不会反向定位到上方 Treemap。用户需要从 AI 推荐项快速回看该文件或目录在磁盘树中的位置，并且点击 Treemap 的“上一级”后能回到点击推荐项前的分析视图。

## 改动

- `RecommendationsPanel` 新增推荐项激活事件：
  - 双击推荐列表行会触发定位。
  - 点击路径文本区域也会触发定位，路径区域使用 `Button` 承载以提供明确可点击行为。
- `MainWindow` 订阅 `RecommendationActivated`，当推荐项包含 `Entry` 时调用 Treemap 导航。
- `TreemapViewModel` 新增 `NavigateToEntry(FileEntry entry)`：
  - 推荐项是目录时直接展示该目录。
  - 推荐项是文件时展示其父目录，使文件所在位置能出现在 Treemap 中。
  - 导航前会把当前 `CurrentRoot` 压入 `_navigationStack`，因此 Treemap 的“上一级”会返回用户点击推荐项前的视图。
  - 只允许导航到当前扫描根 `_scanRoot` 下的节点，避免跨扫描或 stale recommendation 破坏当前视图状态。

## 关键文件

- `src/SpaceMonger.App/Views/RecommendationsPanel.xaml`
- `src/SpaceMonger.App/Views/RecommendationsPanel.xaml.cs`
- `src/SpaceMonger.App/MainWindow.xaml.cs`
- `src/SpaceMonger.App/ViewModels/TreemapViewModel.cs`

## 验证建议

1. 扫描一个目录或磁盘。
2. 点击 `Analyze` / “推荐清理”，等待推荐列表出现。
3. 在 Treemap 先下钻到任意子目录作为当前分析视图。
4. 点击推荐列表中的路径，确认上方 Treemap 跳转到该推荐项目录或文件父目录。
5. 点击 Treemap “上一级”，确认返回第 3 步的原分析视图。
