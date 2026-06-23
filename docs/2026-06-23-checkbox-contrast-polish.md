# 2026-06-23 复选框选中态对比度优化

## 背景

推荐清理列表中的复选框选中后使用半透明蓝底和蓝色勾线，在深色卡片背景上对比度不足，勾选状态不够明显。

## 调整

- 更新 `src/SpaceMonger.App/Themes/VisionProTheme.xaml` 中 `VP.CheckBox` 的选中态样式。
- 选中态改为更明确的蓝色实底，并使用白色勾线提升辨识度。
- 半选态同步改为蓝色底和白色横线，便于区分“已选择部分项”的状态。

## 验证

- 已运行 `dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj`，构建通过。
- 已运行 `dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false`，发布输出到 `outputs/spacemonger-next-20260623-190515/`。
- 构建仍存在项目既有警告：`NU1701` 包兼容警告、nullable 警告，以及 `NoItemsSelectedForCleanupToolTip` 重复资源名警告。
