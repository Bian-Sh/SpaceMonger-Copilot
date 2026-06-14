# 迭代纪要：Treemap 与推荐清理面板可拖拽分隔（2026-06-15）

## 背景

用户反馈 Treemap 位于上方，“推荐清理”分析结果位于下方；当推荐项较多时，下方 ScrollView 内容展示空间不足，需要能通过上下拖拽调整 Treemap 与推荐结果区域的高度。

## 改动

- `MainWindow.xaml` 将主内容左侧区域拆成三行：
  - Treemap 行：`*`，保留最小高度，避免拖拽后地图完全消失。
  - `GridSplitter` 行：推荐面板显示时出现，可上下拖拽。
  - 推荐面板行：默认隐藏，高度为 `0`；分析时展开。
- `MainWindow.xaml.cs` 新增 `ShowRecommendationsPanel()` / `HideRecommendationsPanel()`：
  - 点击“推荐清理”后立即显示推荐面板和分隔条，默认高度 `260`。
  - 点击推荐面板关闭按钮时同时隐藏分隔条，并把推荐面板行高重置为 `0`，不占用 Treemap 空间。
- 推荐清理面板自身不再使用固定 `MaxHeight=300`，用户可以按当前窗口高度拖拽分配更多列表空间。

## 关键文件

- `src/SpaceMonger.App/MainWindow.xaml`
- `src/SpaceMonger.App/MainWindow.xaml.cs`

## 验证建议

1. 扫描目录或磁盘。
2. 点击 `推荐清理`，确认下方推荐结果面板和中间分隔条出现。
3. 鼠标放在 Treemap 与推荐结果之间的横条上，上下拖动。
4. 确认上方 Treemap 和下方推荐列表区域高度随拖拽变化。
5. 点击推荐面板右上角关闭按钮，确认下方区域收起且 Treemap 重新占满左侧高度。
