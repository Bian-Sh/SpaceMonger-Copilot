# 2026-06-20 Chat 折叠按钮 icon 高度微调

## 背景

- 标题栏设置按钮右侧的 Chat 折叠按钮 icon 视觉高度偏矮，需要上下各拉高 2px 试看效果。

## 修改

- `src/SpaceMonger.App/Controls/WindowTitleBar.xaml`
  - 将 `CollapseChatButton` 内部 icon 容器从 `16x16` 调整为 `16x20`。
  - 同步将展开态与折叠态的竖向分隔线 `Y2` 从 `16` 调整为 `20`。

## 验证

- 已运行 `dotnet build src\SpaceMonger.sln`，构建成功。
- 已发布 `win-x64` folder 版到 `outputs\SpaceMonger-win-x64-folder-20260620-123455`，入口为 `SpaceMonger.App.exe`。
- 构建期间仍有既有 `NU1701` 包兼容性警告，以及 `Strings.resx` 中 `NoItemsSelectedForCleanupToolTip` 重复资源名警告；本次未改动这些既有问题。
