# 迭代纪要：顶部按钮与控制台筛选位置调整（2026-06-16）

## 本次需求

- 顶部工具栏不要常驻显示“取消”按钮，取消扫描应放在扫描中的 mask 遮罩中。
- 设置按钮再向右移一点，并移除明显白色背景，让 icon 更纯粹。
- 控制台 log level 菜单不要放在 tab header，切换到控制台 tab 后在内容区右上角展示。

## 实现摘要

- `src/SpaceMonger.App/MainWindow.xaml`
  - 移除 toolbar 内的 `CancelScanButton`。
  - 将 `ConsoleTab` 恢复为简单 `Header="控制台"`。
  - 将 `ConsoleFilterButton` 移到控制台内容 `Grid` 右上角，只在控制台 tab 内可见。
  - 设置按钮右边距从 `34` 改为 `8`，并设为 `Background="Transparent"`、`BorderThickness="0"`。

- `src/SpaceMonger.App/Views/TreemapView.xaml`
  - 在 `ScanningOverlay` 中新增“取消”按钮。
  - 按钮绑定到窗口 `DataContext.CancelScanCommand`，避免 `TreemapView` 自身 DataContext 变化导致绑定丢失。

## 验证

```powershell
dotnet build .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Debug --no-restore
```

- 构建通过。
- 仍只有既有 `NU1701` 包兼容警告。
