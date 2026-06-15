# 迭代纪要：顶部箭头与控制台菜单位置修正（2026-06-16）

## 本次需求

- 上一轮对扫描按钮右下角箭头的理解不准：目标是删掉 WPF `ToolBar` 自带的溢出箭头。
- 上一轮控制台 context menu 新增了一行，抢占了控制台输出区域；应该放到 tab strip 右侧空白区，不增加新行。

## 实现摘要

- `src/SpaceMonger.App/MainWindow.xaml`
  - 移除 `ToolBarTray` / `ToolBar`，改用普通 `DockPanel` 承载顶部导航控件。
  - 顶部导航保留原有路径选择、浏览、扫描按钮，但不再使用 WPF `ToolBar`，因此不再产生扫描按钮右下角的溢出箭头。
  - 将底部工具窗口改成 `Grid` 包裹 `TabControl` 和 `ConsoleToolbarOverlay`。
  - `ConsoleToolbarOverlay` 使用 `Panel.ZIndex=20` 叠加在 tab strip 右侧空白区，不占用控制台输出区域的垂直空间。

- `src/SpaceMonger.App/MainWindow.xaml.cs`
  - 新增 `BottomTabs_SelectionChanged`，只在当前选中 `ConsoleTab` 时显示 `ConsoleToolbarOverlay`。

## 验证

```powershell
dotnet build .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Debug --no-restore
dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-win-x64-folder-20260616-011759
```

- 构建通过。
- 新版 exe 已发布到 `outputs\SpaceMonger-win-x64-folder-20260616-011759`。
- 仍只有既有 `NU1701` 包兼容警告。
