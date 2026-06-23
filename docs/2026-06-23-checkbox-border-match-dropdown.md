# 2026-06-23 复选框描边对齐下拉框

## 背景

复选框选中态改为蓝底白色勾线后，状态辨识度明显提升，但选中态白色描边在推荐清理列表中显得过于突兀。

## 调整

- 将 `src/SpaceMonger.App/Themes/VisionProTheme.xaml` 中 `VP.CheckBox` 的选中态与半选态描边从白色半透明值改为 `VP.BorderLightBrush`。
- `VP.BorderLightBrush` 与 `VP.ComboBox` 默认 `BorderBrush` 一致，使复选框描边和 filter 下拉框保持同一视觉语言。
- 保留白色对勾与白色半选横线，保证状态仍然清楚。

## 验证

- 已运行 `dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj`，构建通过。
- 已运行 `dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false`，发布输出到 `outputs/spacemonger-next-20260623-192036/`。
- 构建仍存在项目既有警告：`NU1701` 包兼容警告、nullable 警告，以及重复资源名警告。
