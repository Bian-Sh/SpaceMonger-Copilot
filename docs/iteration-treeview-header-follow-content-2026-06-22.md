# TreeView Header 跟随内容宽度修复纪要

日期：2026-06-22  
主题：TreeView 横向滚动时 Header 与内容列同步

## 本轮完成

- 为 TreeView Header 增加与内容区一致的列最小宽度，避免 Header 列宽小于实际内容列宽。
- Header 末尾新增 spacer 列，根据 TreeView 内容 `ExtentWidth` 动态补齐宽度。
- 横向滚动时按 TreeView 与 Header 的可滚动宽度比例同步 Header 偏移，降低边框和布局差异造成的错位。
- 使用 `_syncing` 防止 Header 同步过程产生反馈循环。

## 影响范围

- 修改范围集中在 `TreeViewControl.xaml` 与 `TreeViewControl.xaml.cs`。
- 本次提交不包含其它未提交的 Treemap 圆角、启动参数等改动。
