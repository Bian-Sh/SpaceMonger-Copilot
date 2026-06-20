# 迭代纪要：文件夹选择后的导航联动修复（2026-06-18）

## 背景

延续 docs/todo-navigation-treemap-followups-2026-06-17.md 中的第 1 项 TODO：用户通过导航栏文件夹按钮选择路径后，地址栏、Treemap 当前根、扫描根节点之间存在状态分裂，导致地址栏不一定立即展示所选路径，后续扫描也可能被旧 Treemap 当前目录劫持。

## 本次改动

- 在 src/SpaceMonger.App/MainWindow.xaml.cs 中引入 _displayPathOverride，让“用户显式选择但不在当前扫描树内的路径”成为面包屑显示路径。
- 将 MainViewModel.SelectedPath 变化接入 NavigateToPathOrSelect()，让文件夹选择、地址栏/面包屑导航共用同一入口。
- 增加 _suppressSelectedPathNavigation，避免程序化同步 SelectedPath 时递归触发导航。
- 将扫描目标委托集中到 MainWindow.GetScanTargetPath()：外部选择路径优先；否则 Treemap 钻入扫描根以下目录时仍可作为扫描目标。
- 删除 src/SpaceMonger.App/App.xaml.cs 中旧的 GetCurrentViewPath 赋值，避免覆盖窗口侧统一逻辑。

## 期望行为

- 通过导航栏文件夹按钮选择文件夹后，地址栏/面包屑立即展示该路径。
- 如果路径在当前扫描树内，Treemap 跳转到该目录。
- 如果路径不在当前扫描树内，Treemap 当前根变为外部路径，画布无数据并显示“需重新分析/无数据”状态。
- 用户随后点击扫描时，扫描目标优先使用刚选择的外部路径，而不是旧的 Treemap 当前目录。

## 验证方式

- 编译验证：dotnet build .\src\SpaceMonger.sln --no-restore
- 手动验证建议：先扫描一个目录，再通过文件夹按钮选择扫描树内子目录和扫描树外目录，分别确认地址栏、Treemap 和再次扫描目标是否一致。
