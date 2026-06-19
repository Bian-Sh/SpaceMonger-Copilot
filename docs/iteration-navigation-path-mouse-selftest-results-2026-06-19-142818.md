# 导航路径一致性鼠标级自测结果（2026-06-19-142818）

## 测试环境

- 测试样本目录：C:\tmp\sm-nav-path-test
- 测试包：outputs\SpaceMonger-win-x64-folder-20260619-142705-selftest5\SpaceMonger.App.exe
- 启动方式：SPACEMONGER_ACCEPTANCE_SERVER=1，用于读取状态和最后扫描路径。
- 交互方式：Computer Use 真实窗口截图坐标点击 + 键盘输入；acceptance TCP 只用于读取 SelectedPath、CurrentRootPath、CurrentSessionTargetPath 等诊断状态。

## 本轮实际执行用例

| 用例 | 真实操作 | 结果 |
| --- | --- | --- |
| NAV-002 | 点击地址栏空白 → 输入 C:\tmp\sm-nav-path-test → Enter → 鼠标点 Scan | 通过，CurrentSessionTargetPath=C:\tmp\sm-nav-path-test |
| NAV-003 | 扫描样本根后，鼠标双击 Treemap 中 A/A1 区域 | 通过，SelectedPath 与 CurrentRootPath 同步到 C:\tmp\sm-nav-path-test\A\A1 |
| MIX-002 | 已在 Treemap 下钻到旧 A1 后，地址栏输入 C:\tmp\sm-nav-path-test\B\B1 → Scan | 通过，实际扫描 B1，不再扫描旧 A1 |
| EDGE-001/002 | 地址栏输入带前后空格、尾斜杠的 A1Leaf\ → F5 | 通过，实际扫描 C:\tmp\sm-nav-path-test\A\A1\A1Leaf\，未回扫 B1 |
| NAV-005 | 从 A1Leaf 点击上级按钮到 A1 → Scan | 通过，实际扫描 A1 |
| NAV-001 / MIX-001 | 鼠标打开 folder picker → 在系统选择器输入并选择 C:\tmp\sm-nav-path-test\B\B1 → Scan | 通过，folder picker 覆盖旧路径，实际扫描 B1 |
| EDIT-002 | 地址栏编辑新路径但不按 Enter，直接鼠标点击 Scan | 首次失败，修复后通过，实际扫描新编辑路径 A1Leaf |

## 测试中发现的真实问题

### EDIT-002 失败

复现步骤：

1. 当前已扫描 C:\tmp\sm-nav-path-test\B\B1。
2. 点击地址栏进入编辑态。
3. 输入 C:\tmp\sm-nav-path-test\A\A1\A1Leaf。
4. 不按 Enter，直接点击 Scan。

失败表现：

- 修复前第一次鼠标点击 Scan 只让编辑框失焦/退出编辑态，SelectedPath 仍是旧 B1。
- 后续扫描目标仍可能是旧路径 B1，符合用户描述的“我明明导航/输入了一个路径，扫描却扫了之前某个路径”。

根因：

- Window_PreviewMouseLeftButtonDown 在编辑态点击 Scan 时先执行，直接 ExitPathEditMode()，导致编辑文本没有提交。
- Scan 按钮的 Command 执行时看到的仍是旧 SelectedPath。

## 本轮新增修复

- src/SpaceMonger.App/MainWindow.xaml
  - ScanButton 增加 PreviewMouseLeftButtonDown="ScanButton_PreviewMouseLeftButtonDown" 与 Click="ScanButton_Click"。

- src/SpaceMonger.App/MainWindow.xaml.cs
  - 提取 CommitPathEditText()，Enter、F5、Scan 点击统一提交地址栏编辑文本。
  - Window_PreviewMouseLeftButtonDown 遇到 Scan 按钮时先提交编辑文本，再立即执行 ScanCommand，并设置 e.Handled=true，避免旧路径命令先跑。
  - F5 扫描前也调用 CommitPathEditText()，覆盖“编辑后直接 F5”的同类场景。
  - acceptance state 增加 IsScanning、CurrentSessionTargetPath，用于鼠标级自测强断言实际扫描路径。

## 最终复测关键断言

最后一轮 EDIT-002 复测结果：

`	ext
before:
  SelectedPath = C:\tmp\sm-nav-path-test\B\B1
  BreadcrumbMode = edit
  CurrentSessionTargetPath = C:\tmp\sm-nav-path-test\B\B1

after:
  SelectedPath = C:\tmp\sm-nav-path-test\A\A1\A1Leaf
  CurrentRootPath = C:\tmp\sm-nav-path-test\A\A1\A1Leaf
  CurrentSessionTargetPath = C:\tmp\sm-nav-path-test\A\A1\A1Leaf
  BreadcrumbMode = breadcrumb

result: PASS
`

## 发布包

- 最新测试通过发布目录：outputs\SpaceMonger-win-x64-folder-20260619-142705-selftest5
- 启动程序：outputs\SpaceMonger-win-x64-folder-20260619-142705-selftest5\SpaceMonger.App.exe

注意：这是 folder publish，需要整个目录一起分发，不要只复制单个 exe。
