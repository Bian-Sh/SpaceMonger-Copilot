# 2026-06-23 推荐清理分析进度蒙版调整

## 背景

推荐清理窗口进入分析状态时，进度提示的外层蒙版使用了不透明的 `VP.BackgroundAltBrush`，在底部 Tab 内容区顶部形成明显的覆盖边沿。视觉上会贴近 Tab，并让外层内容框阴影显得更突兀。

## 调整

- 将 `src/SpaceMonger.App/Views/RecommendationsPanel.xaml` 中分析态 overlay 的背景从 `VP.BackgroundAltBrush` 改为 `Transparent`。
- 移除该 overlay 的底部圆角设置，避免生成一整块可见覆盖层。
- 增加顶部 `Padding="0,8,0,0"`，让进度提示与 Tab 下沿保持更自然的呼吸感。

## 验证

- 已运行 `dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj`，构建通过。
- 已运行 `dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false`，发布输出到 `outputs/spacemonger-next-20260623-185020/`。
- 构建仍存在项目既有警告：`NU1701` 包兼容警告、部分 nullable 警告，以及 `NoItemsSelectedForCleanupToolTip` 重复资源名警告。
