# 推荐清理筛选栏选择布局调整

日期：2026-06-23

## 背景

用户反馈推荐清理页顶部筛选区与下方内容区缺少视觉分隔，且批量选择功能与过滤器混排，复选框选择入口也没有与列表 cell 左侧复选框形成同一视觉列。

## 改动

- 将 `RecommendationsPanel` 顶部筛选栏由裸 `DockPanel` 改为带低对比背景、细边框和圆角的容器，增强与内容列表的区分但保持整体灰紫主题一致。
- 新增顶部三态复选框，绑定 `AllRecommendationsSelectionState`：全选显示勾选、全不选显示空、部分选择显示横线。
- 新增 `ToggleAllRecommendationsCommand`，点击顶部三态复选框可在全选和全不选之间切换。
- 将“全选安全项”和“剔除风险项”与顶部三态复选框归为左侧选择模块；类别/评级过滤器移动到右侧。
- 将操作按钮垂直 padding 从 `8,4` 压缩为 `8,2`，筛选栏上下 padding 压缩为 `2px`。
- 将“取消选择谨慎项”文案更新为“剔除风险项”，英文对应为 `Exclude Risk Items`。
- 给 `VP.CheckBox` 模板补充 `IsChecked=null` 的横线半选视觉。

## 验证

- 已执行 `npx @colbymchenry/codegraph sync` 同步代码索引。
- 已执行 `dotnet build src\SpaceMonger.sln -c Release`，构建通过；保留项目既有 NU1701、重复资源名和 nullable 警告。

---

## 第二轮调整（2026-06-23 v2）

用户反馈：
1. 筛选栏圆角与外框形成套娃感，全去圆角。
2. 三态复选框没有与列表 cell 复选框垂直方向对齐。
3. ScrollView 底部再次出现点击拦截。

### 改动

- 去掉筛选栏 `CornerRadius="7"`，只保留背景和浅边框区分。
- 将筛选栏左边距从 `Padding="10,2"` 调整为 `Padding="14,2,10,2"`，使三态复选框与 cell 的 `Padding="10,6"` + `ListViewItem.Padding="4"` + item `Margin="2"` 计算对齐于同一 X 坐标（`20px`）。
- 在 `ListViewItem` 的 `ItemContainerStyle` 中补上 `EventSetter Event="RequestBringIntoView"`，将先前仅存在于 code-behind 但未绑定的事件重新连接。WPF 默认 `RequestBringIntoView` 会在点击底部可见项时自动滚动画布，导致点击被解释为滚动而非选择。现通过 `RecommendationItem_RequestBringIntoView` 中 `e.Handled = true` 彻底拦截。

---

## 第三轮微调（2026-06-23 v3）

用户反馈：筛选区背景左右仍比推荐清理窗口内边缘缩进约 3–4px；三态复选框与 cell 复选框仍未在同一 X 轴；左侧批量操作按钮高度需恢复到与右侧 filter 控件一致。

### 改动

- 将筛选栏外边距从 `Margin="4,2,4,4"` 改为 `Margin="0,2,0,4"`，让背景左右贴合推荐清理面板内边缘。
- 将筛选栏内边距从 `Padding="14,2,10,2"` 改为 `Padding="20,2,10,2"`，让三态复选框与列表 cell 复选框的 X 坐标一致。
- 将“全选安全项”和“剔除风险项”按钮恢复为 `Padding="8,4"`，与右侧 filter 高度保持一致。
