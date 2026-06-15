# 迭代纪要：推荐清理面板交互修复（2026-06-15）

## 背景

本轮处理“推荐清理”模块的交互与统计问题：顶部工具栏入口需要移除，推荐清理 Tab 内部需要承载分析入口；推荐列表需要更易核对条目数量；勾选后的已选数量和可释放空间此前不会随 CheckBox 变化刷新；筛选器选择“所有类别 / 所有评级”时会错误过滤掉数据。

## 改动

- 移除 App 顶部工具栏的 `推荐清理` 按钮，保留 `Ctrl+Shift+A` 快捷键，并让快捷键继续走同一套分析逻辑。
- 删除 `RecommendationsPanel` 右侧关闭按钮，替换为 `分析` 按钮；该按钮触发原顶部 `推荐清理` 的完整分析流程，包括扫描数据检查、API Key 检查、重分析确认、分析范围设置、诊断日志和状态栏更新。
- 推荐列表每个 cell item 增加 `Id` 显示列，方便从 UI 直接核对推荐条目编号与数量。
- 推荐列表启用 `AlternationCount=2`，通过 `ListViewItem` 背景色区分奇偶行，并保留选中行高亮。
- `CheckBox` 勾选 / 取消时调用 `RecommendationsViewModel.UpdateTotals()`，修复底部 `已选` 与 `可释放` 长期为 0 的问题。
- `CleanupRecommendation` 实现 `INotifyPropertyChanged`，让 `IsAccepted` / `IsDismissed` 的互斥变化能通知 UI，批量选择与取消状态更稳定。
- 筛选器绑定从 nullable enum 改为 `object?`，只在选中项实际是 `RecommendationCategory` 或 `SafetyRating` 时过滤；“所有类别 / 所有评级”的 `ComboBoxItem` 不再被误当作 enum 条件。

## 关键文件

- `src/SpaceMonger.App/MainWindow.xaml`
- `src/SpaceMonger.App/MainWindow.xaml.cs`
- `src/SpaceMonger.App/Views/RecommendationsPanel.xaml`
- `src/SpaceMonger.App/Views/RecommendationsPanel.xaml.cs`
- `src/SpaceMonger.App/ViewModels/RecommendationsViewModel.cs`
- `src/SpaceMonger.Core/Models/CleanupRecommendation.cs`
- `src/SpaceMonger.App/Localization/Strings.resx`
- `src/SpaceMonger.App/Localization/Strings.zh-CN.resx`
- `tests/SpaceMonger.App.Tests/RecommendationsViewModelTests.cs`

## 验证

已执行：

```powershell
dotnet build .\src\SpaceMonger.sln --no-restore
```

结果：构建成功，`0` errors。仍有既有 `NU1701` package compatibility warnings，涉及 `OpenTK`、`OpenTK.GLWpfControl`、`SkiaSharp.Views.WPF`，本轮未改动这些依赖。

## 手动回归建议

1. 启动应用，确认顶部工具栏不再显示 `推荐清理`。
2. 完成扫描后，在底部 `推荐清理` Tab 点击 `分析`，确认仍可触发原推荐分析流程。
3. 有结果后确认每行显示 `Id`，行背景色奇偶交替。
4. 勾选任意推荐项，确认底部 `已选` 和 `可释放` 立即刷新。
5. 分别选择 `所有类别`、`所有评级`，确认恢复显示完整推荐列表；选择具体类别 / 评级时才收窄列表。


## 2026-06-16 继续修复

- `ListViewItem` 增加 `RequestBringIntoView` 事件拦截，避免点击底部可见 cell 时 WPF 自动把该项滚到视口内，造成点击被表现成 ScrollView 向下滚动。
- 推荐项编号从 `REC-001` 改为纯数字字符串，例如 `1`、`2`、`3`，并在 cell 最前方展示。
- cell 布局调整为：编号、路径、大小、评级、复选框；复选框移动到最右侧。
- `分析` 按钮绑定 `IsAnalyzing` 状态：分析中禁用按钮，并通过 `ToolTipService.ShowOnDisabled=True` 在 hover 时显示 `分析中请等待`。
- `OnAnalyzeRequested()` 增加二次 guard，防止快捷键或其他入口在分析中重复触发。

补充验证：

```powershell
dotnet test .\src\SpaceMonger.sln --no-restore
```

结果：测试通过。`SpaceMonger.Core.Tests` 通过 `2` 个测试，`SpaceMonger.App.Tests` 通过 `1` 个测试。仍有既有 `NU1701` warnings。
