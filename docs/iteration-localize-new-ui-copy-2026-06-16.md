# 迭代纪要：新增 UI 文案本地化修复（2026-06-16）

## 问题

- 近期新增的空态、tab header、tooltip 等 UI 文案部分直接写在 XAML 里，切换语言时不会跟随资源切换。

## 修复

- `src/SpaceMonger.App/Localization/Strings.resx`
  - 新增英文资源键：`OpenConsoleToolTip`、`RecommendationsTabHeader`、`ConsoleTabHeader`、`ConsoleFilterToolTip`、`TreemapNoDataText`、`ChatEmptyTitle`、`ChatEmptyDescription`、`RecommendationsEmptyTitle`、`RecommendationsEmptyDescription`。

- `src/SpaceMonger.App/Localization/Strings.zh-CN.resx`
  - 新增对应简体中文资源键。

- `src/SpaceMonger.App/MainWindow.xaml`
  - 将“推荐清理”、“控制台”、“打开控制台”、“筛选控制台日志”改为 `{loc:Loc ...}` 绑定。

- `src/SpaceMonger.App/Views/TreemapView.xaml`
  - 将 treemap 空态文案改为 `TreemapNoDataText`。

- `src/SpaceMonger.App/Views/ChatPanel.xaml`
  - 将聊天空态标题和说明改为 `ChatEmptyTitle` / `ChatEmptyDescription`。

- `src/SpaceMonger.App/Views/RecommendationsPanel.xaml`
  - 将推荐清理空态标题和说明改为 `RecommendationsEmptyTitle` / `RecommendationsEmptyDescription`。

## 验证

```powershell
dotnet build .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Debug --no-restore
dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-win-x64-folder-20260616-013540
```

- 构建通过。
- 新版 exe 已发布到 `outputs\SpaceMonger-win-x64-folder-20260616-013540`。
- 仍只有既有 `NU1701` 包兼容警告。
