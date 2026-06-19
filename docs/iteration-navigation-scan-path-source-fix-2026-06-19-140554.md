# 导航栏扫描路径来源修复迭代（2026-06-19-140554）

## 背景

用户反馈导航栏输入框、folder picker、TreeView 导航三类路径状态混乱：在导航到较远路径后执行扫描，实际扫描目标可能变成历史阶段出现过的其他路径；folder picker 选择路径后也偶发扫描到旧路径。

## 根因

MainViewModel.ScanAsync() 通过 GetCurrentViewPath 回调从窗口层反查当前 Treemap 视图路径，并优先使用该返回值作为扫描目标。窗口层 GetScanTargetPath() 又同时读取 _displayPathOverride、SelectedPath、TreemapViewModel.CurrentRoot，导致：

- folder picker 只更新 SelectedPath，但旧 CurrentRoot 仍可能在扫描时参与决策。
- 手动输入路径后，外部路径、已扫描树内路径、当前视图路径之间存在多套优先级。
- TreeView/面包屑导航变化没有稳定地把最终路径同步回 SelectedPath，扫描入口可能拿到过期路径。

## 修改

- src/SpaceMonger.App/ViewModels/MainViewModel.cs
  - 移除 GetCurrentViewPath 回调。
  - ScanAsync() 改为只使用 SelectedPath 作为扫描目标，并在扫描前 Trim()。
  - CanScan() 仍由 SelectedPath 和 scanner ready 状态控制。

- src/SpaceMonger.App/MainWindow.xaml.cs
  - 移除 mainVm.GetCurrentViewPath = GetScanTargetPath 绑定。
  - 删除窗口层 GetScanTargetPath() 的多源路径决策。
  - TreemapViewModel.CurrentRoot 变化时，将 CurrentRoot.Path 同步回 SelectedPath，使 TreeView/面包屑导航结果也进入同一个扫描源。

## 当前规则

扫描入口现在遵循单一事实来源：MainViewModel.SelectedPath。

- folder picker：设置 SelectedPath。
- 手动地址栏输入：通过 NavigateToPathOrSelect() 设置 SelectedPath。
- TreeView/面包屑/返回前进上级：CurrentRoot 变化后同步 SelectedPath。
- Scan/F5：只扫描 SelectedPath，不再反查旧 Treemap 视图或 _displayPathOverride。

## 验证

- 已执行：dotnet build src/SpaceMonger.sln -c Release
- 结果：构建成功，0 errors。
- 仍有既有 NU1701 包兼容 warning，和本次改动无关。

## 注意

本次是路径状态源头修复，不改动中文资源和无关 UI 文案。后续若要做鼠标级验收，可用 acceptance TCP 状态中的 SelectedPath 与 CurrentRootPath 对照确认。
