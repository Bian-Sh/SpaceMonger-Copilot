# 迭代纪要：恢复 TreeView Header 与内容分割线锁定（2026-06-23）

## 背景

用户反馈 TreeView 中左右拖动 Header 分割线后，Header 分割线又与 TreeView content cell 的分割线发生视觉重合，疑似此前 Header/content 横向同步与锁定修复被近期样式调整覆盖。

## 根因

近期统一上方板块样式时，`TreeViewControl.xaml` 的 Header 和内容区边框/背景被改为新的 `SurfaceHoverBrush` / `SurfaceBrush` / `BorderLightBrush`，并给内容区增加阴影。该改动没有修改列宽同步逻辑本身，但改变了 Header 与 content 的视觉边界，使之前 Header/content 分割线锁定修复后的边界再次表现为重合。

## 修改

- 恢复 TreeView Header 的原边框层级：`VP.BackgroundBrush`、`VP.BorderSubtleBrush`、顶部边框、无底部边框。
- 恢复 TreeView content 区域的原背景/边框资源。
- 移除 TreeView content 区域新增阴影，避免阴影参与视觉边界干扰。
- 未改动 2026-06-22 的 Header/content 宽度同步、spacer 和横向滚动比例同步逻辑。

## 验证

- `dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj -c Debug` 通过。
- 已发布并启动 `outputs/SpaceMonger.App-2026-06-23-081748/SpaceMonger.App.exe` 供现场验收。
