# 迭代纪要：按最终经验恢复 TreeView 比例滚动同步（2026-06-23）

## 背景

用户指出 TreeView 横向 slider 拖动错位仍存在，并要求参考 `docs/iteration-treeview-scroll-sync-final-fix-2026-06-23.md` 的经验修复，同时不要修改外观样式相关设定。

## 修改

- 不修改 `TreeViewControl.xaml` 中背景、边框、阴影、颜色、字体等外观样式设定。
- 保留 Header spacer 列与 deferred sync 结构。
- 将上一轮错误尝试的绝对 offset 同步恢复为最终经验中的比例同步：
  - `ratio = treeSv.HorizontalOffset / treeSv.ScrollableWidth`
  - `headerOffset = ratio * HeaderScrollViewer.ScrollableWidth`
- `_syncing` 仍仅包裹 Header 编程滚动，避免拦截 TreeView 自身 `ScrollChanged`。

## 验证

- `dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj -c Debug` 通过。
- 已发布并启动 `outputs/SpaceMonger.App-2026-06-23-083211/SpaceMonger.App.exe`。
