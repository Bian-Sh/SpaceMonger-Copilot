# 迭代纪要：TreeView 横向 slider 同 offset 同步（2026-06-23）

## 背景

用户确认点击 `分析` 后不再切换控制台，但 TreeView 横向 slider 拖动后 Header 分割线与 content cell 分割线错位依旧。

## 根因

既有同步逻辑为了处理 Header 与内容区可滚动宽度差异，使用 `HeaderOffset = TreeOffset / TreeScrollableWidth * HeaderScrollableWidth` 的比例映射。横向 slider 拖动时，Header 和 TreeView 的 extent/scrollable width 存在来自边框、spacer、模板的微小差异，比例映射会把该差异转化为可见列线偏移。

## 修改

- 保持 Header 着色、content 样式和分析切页修复不变。
- TreeView 横向滚动同步改为同 offset：`HeaderScrollViewer.ScrollToHorizontalOffset(treeSv.HorizontalOffset)`。
- 继续保留 Header spacer 计算，使 Header 内容宽度覆盖 TreeView extent。

## 验证

- `dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj -c Debug` 通过。
- 已发布并启动 `outputs/SpaceMonger.App-2026-06-23-082621/SpaceMonger.App.exe`。
