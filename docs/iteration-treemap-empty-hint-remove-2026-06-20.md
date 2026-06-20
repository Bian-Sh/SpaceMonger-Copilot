# 迭代纪要：Treemap 空态移除全部 Hint 提示（2026-06-20）

## 背景

用户反馈 Treemap 空态 Hint 提示多余，要求仅保留图标与标题。上一版只处理了默认空态，遗漏了 Scan required / No child data 状态的 Hint。

## 修改内容

- `TreemapView.xaml`: EmptyStateHint 默认 `Visibility=Collapsed`。
- `TreemapView.xaml.cs`: `UpdateEmptyState()` 移除所有 EmptyStateHint 赋值逻辑，仅保留 Title 切换。
- `Strings.resx`: `TreemapEmptyHint`、`TreemapAnalysisRequiredHint`、`TreemapNoChildDataHint` 值清空。
- `Strings.zh-CN.resx`: 同上。

## 验证

- `dotnet build`: 0 错误。
- `dotnet publish ... -o outputs\spacemonger-next-emptyhint-all-20260620-151001`: 通过。

## 输出

- 发布目录: `outputs\spacemonger-next-emptyhint-all-20260620-151001`
