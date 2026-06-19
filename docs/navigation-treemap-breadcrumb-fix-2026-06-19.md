# 2026-06-19 面包屑路径尾斜杠导致 treemap 空状态修复

## 背景

用户在完成 `C:\` 扫描后，从 treemap 逐级进入 `C:\Users\BianShanghai\AppData\Local\Temp\Thunder Network`，再点击面包屑中的 `Local` 时，treemap 错误进入“没有可显示子项”状态；随后点击向上导航也无法恢复扫描树内的 treemap。

## 根因

- `MainWindow.ParsePathSegments` 给非末尾面包屑段生成的路径带尾部 `\`，例如 `C:\Users\BianShanghai\AppData\Local\`。
- `TreemapViewModel.FindEntryByPath` 之前使用原始字符串比较，扫描树里的 `FileEntry.Path` 通常是不带尾部 `\` 的 `C:\Users\BianShanghai\AppData\Local`，因此无法命中扫描树节点。
- 命中失败后 `NavigateToPathOrSelect` 会走 `NavigateToExternalPath`，创建一个无子项的外部目录节点，于是 treemap 显示空状态，后续向上也沿着外部节点继续导航。

## 修改

- 在 `TreemapViewModel` 的扫描树路径查找中加入可比较路径归一化：保留盘符根路径，非根路径去掉尾部 `\`/`/` 后再做 `OrdinalIgnoreCase` 比较。
- 新增 `TreemapViewModelTests.NavigateToPath_TrailingSeparatorFromBreadcrumb_UsesScannedEntry`，覆盖 `Local\` 这类面包屑尾斜杠路径必须命中扫描树节点。

## 验证

- 运行：`dotnet test src\SpaceMonger.sln --no-restore --filter TreemapViewModelTests`
- 结果：测试通过；手工验证时，扫描 `C:\` 后进入 `...\Local\Temp\Thunder Network`，点击 `Local` 应显示 `Local` 的扫描树 treemap，并且向上导航继续保持在扫描树内。