# 迭代纪要：Treemap 空态标题统一（2026-06-20）

## 背景

用户要求 Treemap 所有空态标题统一：中文 待扫描，英文 Scan required。不再区分 需要重新分析 / 没有可显示的子项 等状态。

## 修改内容

- `Strings.resx`: `TreemapEmptyTitle` `Awaiting Scan` → `Scan required`；`TreemapNoChildDataTitle` → `Scan required`。
- `Strings.zh-CN.resx`: `TreemapAnalysisRequiredTitle` `需要重新分析` → `待扫描`；`TreemapNoChildDataTitle` `没有可显示的子项` → `待扫描`。
- `TreemapView.xaml.cs`: `UpdateEmptyState()` 简化为直接取 `TreemapEmptyTitle`。

## 验证

- `dotnet build`: 0 错误。
- `dotnet publish ... -o outputs\spacemonger-next-emptytext-unify-20260620-151753`: 通过。
