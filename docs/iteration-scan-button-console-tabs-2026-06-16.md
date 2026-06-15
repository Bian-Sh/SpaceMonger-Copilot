# 迭代纪要：扫描按钮和控制台标签区修正（2026-06-16）

## 本次需求

- 扫描按钮过大，需要恢复到更紧凑的尺寸。
- 推荐清理和控制台 tab 中文显示成问号，需要修复为正确 UTF-8 中文。
- tab strip 右侧不需要 `Log Level` 文字。
- 右侧菜单图标应是竖着的三个点（`⋮`），不是问号。

## 实现摘要

- `src/SpaceMonger.App/MainWindow.xaml`
  - `ScanButton` 设置为 `Width=50`、`Height=28`、`Padding=0`。
  - 重写 `RecommendationsTab` / `ConsoleTab` header 为真实 Unicode 中文：“推荐清理”、“控制台”。
  - 移除 `ConsoleToolbarOverlay` 里的 `Log Level` 文字。
  - `ConsoleFilterButton.Content` 修正为 Unicode `⋮`，并保持右侧 tab strip 空白区叠加布局。

## 验证

```powershell
dotnet build .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Debug --no-restore
dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-win-x64-folder-20260616-012827
```

- 构建通过。
- 新版 exe 已发布到 `outputs\SpaceMonger-win-x64-folder-20260616-012827`。
- 仍只有既有 `NU1701` 包兼容警告。
