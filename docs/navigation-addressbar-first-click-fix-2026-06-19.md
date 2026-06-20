# 2026-06-19 地址栏编辑态退出后首击面包屑无响应修复

## 背景

复现路径：完成 `C:\` 扫描并显示 treemap 后，逐级进入 `C:\Users\BianShanghai\AppData\Local\Temp\Thunder Network`；点击导航栏输入框空白处让地址栏进入常规输入框；再点击输入框外层让地址栏回到面包屑/dropdown 形式；此时首次点击 `Local` 无响应，第二次点击才可能触发导航。

## 根因

地址栏进入编辑态后，`PathEditTextBox` 覆盖面包屑区域。旧逻辑在 `Window_PreviewMouseLeftButtonDown` 中只要发现点击仍在 `AddressBarBorder` 内，就直接保留焦点并返回；而 `AddressBar_MouseLeftButtonUp` 在编辑态也直接返回。这会让点击输入框外层退出编辑态的行为不稳定，下一次面包屑点击可能先被用于完成焦点/显示状态切换，而不是触发 `BreadcrumbSegment_Click`。

## 修改

- 在 `Window_PreviewMouseLeftButtonDown` 的 Preview 阶段，如果地址栏仍处于编辑态，并且点击在 `AddressBarBorder` 内但不在 `PathEditTextBox` / `BrowseButton` 内，则立即 `SwitchToBreadcrumbMode()`、`Keyboard.ClearFocus()`，并设置 `e.Handled = true`。
- 在 `AddressBar_MouseLeftButtonUp` 保留同类兜底逻辑，避免 Preview 未覆盖的鼠标抬起路径重新吞掉点击或反向切回编辑态。
- 抽出 `IsOriginalSourceWithin`，统一判断点击原始源是否位于目标控件内部。

## 验证

- 运行：`dotnet test src\SpaceMonger.sln --no-restore --filter TreemapViewModelTests`
- 手工回归步骤：扫描 `C:\`；进入 `C:\Users\BianShanghai\AppData\Local\Temp\Thunder Network`；点击地址栏空白处进入输入框；点击输入框外层退出；首次点击 `Local` 应直接导航到 `Local` 的 treemap。