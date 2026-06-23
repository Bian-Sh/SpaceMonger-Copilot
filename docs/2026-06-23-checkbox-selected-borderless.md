# 2026-06-23 复选框选中态去描边

## 背景

复选框选中态保留蓝底白色对勾后，状态已经足够醒目。额外描边会让推荐清理列表里的复选框显得偏重。

## 调整

- 将 `src/SpaceMonger.App/Themes/VisionProTheme.xaml` 中 `VP.CheckBox` 的选中态和半选态 `BorderBrush` 改为 `Transparent`。
- 未选中态仍保留默认描边，用于表达可交互边界。
- 选中态继续保留蓝色底、白色对勾；半选态继续保留蓝色底、白色横线。

## 验证

- 已运行 `dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj`，构建通过。
- 已运行 `dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false`，发布输出到 `outputs/spacemonger-next-20260623-192658/`。
- 构建仍存在项目既有警告：`NU1701` 包兼容警告和 nullable 警告。
