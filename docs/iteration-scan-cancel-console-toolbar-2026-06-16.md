# 迭代纪要：扫描取消与控制台工具条修正（2026-06-16）

## 本次需求

- 修复扫描 mask 中新增“取消”按钮点击不了的问题。
- 控制台 context menu 放到类 Unity Inspector 的窗口工具条位置，不要锁头，背景不要黑色。
- 剔除扫描按钮右下角的 ToolBar 溢出下拉箭头。

## 实现摘要

- `src/SpaceMonger.App/Views/TreemapView.xaml`
  - 移除 `ScanningOverlay` 上的 `IsHitTestVisible="False"`，让遮罩内按钮可接收鼠标事件。
  - 保留“取消”按钮对 `DataContext.CancelScanCommand` 的绑定。

- `src/SpaceMonger.App/MainWindow.xaml`
  - 将控制台内容改为 `DockPanel`，顶部新增浅色 `Border` 作为工具条。
  - 工具条左侧显示 `Log Level`，右侧放置竖点 `ConsoleFilterButton`。
  - 移除之前覆盖在黑色控制台区域右上的筛选按钮。
  - 在 `ToolBar` 上设置 `ToolBar.OverflowMode="Never"`，隐藏扫描按钮旁的溢出箭头。

## 验证

```powershell
dotnet build .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Debug --no-restore
```

- 构建通过。
- 仍只有既有 `NU1701` 包兼容警告。
