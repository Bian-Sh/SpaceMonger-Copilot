# 迭代纪要：大文件职责拆分（2026-06-20）

## 背景

用户要求检查代码是否需要重构，并避免单文件代码量高到离谱。经行数统计和 codegraph 检查，`MainWindow.xaml.cs` 达到 1322 行，混合窗口外壳、ViewModel 绑定、底部面板、控制台、面包屑导航、弹窗/清理、验收自动化入口等职责；`RecommendationEngine.cs` 与 `IncrementalFileScanner.cs` 也接近 750 行。

## 本次调整

- 将 `MainWindow` 保持为同一个 WPF partial 类型，按职责拆分为：窗口外壳、ViewModel/面板绑定、控制台、导航、弹窗/清理、验收自动化。
- 将 `RecommendationEngine` 拆分为主流程、元数据构建、响应解析、路径安全判断。
- 将 `IncrementalFileScanner` 拆分为主扫描入口、MFT 快路径、状态索引、USN 变更应用。
- 本次重构是机械拆分，不改变公开 API 与运行逻辑。

## 文件规模结果

- `src/SpaceMonger.App/MainWindow.xaml.cs`：约 1322 行降为 99 行；最大 MainWindow partial 为 `MainWindow.Navigation.cs`，约 580 行。
- `src/SpaceMonger.Core/Services/Analysis/RecommendationEngine.cs`：约 749 行降为 185 行；最大拆分文件约 289 行。
- `src/SpaceMonger.Core/Services/Scanning/IncrementalFileScanner.cs`：约 747 行降为 132 行；最大拆分文件约 343 行。
- 当前最大源码类文件为 `MainWindow.Navigation.cs`（约 580 行）。`VisionProTheme.xaml` 为资源字典，仍约 809 行，后续如继续 UI 主题化可再拆 ResourceDictionary。

## 验证

- 已运行 `dotnet build src\SpaceMonger.sln`，通过；保留既有 `NU1701` 包兼容警告与 `Strings.resx` 重复资源名警告。
- 已运行 `dotnet test src\SpaceMonger.sln --no-build`，通过：Core 2 个测试、App 2 个测试。
- 已执行 `dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-refactor-mainwindow-services-20260620-093803-win-x64`。

## 发布产物

- `outputs/SpaceMonger-refactor-mainwindow-services-20260620-093803-win-x64/SpaceMonger.App.exe`
- 该发布方式是 folder publish，需要整个目录一起分发，不要只复制单个 exe。

## 后续建议

- 若要进一步收敛文件规模，可优先拆 `VisionProTheme.xaml` 为颜色/控件样式/弹窗样式等多个 ResourceDictionary。
- `MainWindow.Navigation.cs` 仍可继续拆成地址栏编辑与面包屑下拉两部分，但本次先保持低风险机械拆分。
