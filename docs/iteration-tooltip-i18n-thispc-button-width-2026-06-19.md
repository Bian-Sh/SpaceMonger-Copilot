# 迭代纪要：tooltip 多语言修复 + "此电脑"国际化 + 按钮宽度对齐 — 2026-06-19

## 问题
1. 扫描按钮宽度与清理/分析按钮不一致
2. 清理和分析按钮的 tooltip 切换语言后不更新（始终显示中文）
3. 英文环境下导航栏仍显示"此电脑"

## 根因分析

### Tooltip 多语言失效
`{loc:Loc ...}` 在 `Style.Setter` 内部创建 `LocBinding` 时，target 是 `Setter` 而非 `Button`。`LocBinding.OnLanguageChanged` 会更新 `Setter.Value`，但 WPF Style 系统不会在 Setter.Value 变更后重新应用到目标元素，导致 tooltip 始终停留在首次加载时的语言。

**修复**：将默认 ToolTip 从 `Style.Setter` 移到 Button 直接属性 `ToolTip="{loc:Loc ...}"`，此时 `LocBinding` 直接更新 `Button.ToolTipProperty`，语言切换有效。

> DataTrigger 内的 ToolTip setter 仍有此限制，但状态切换时（如 TotalSelectedCount 0→1）会重新应用，属于可接受的临时态。

### "此电脑" 硬编码
`private const string ThisPC = "此电脑"` 同时承担**显示文本**和**内部哨兵值**两个角色，无法直接国际化。

**修复**：
- 新增 `ThisPCSentinel = "::thispc::"` 用于所有内部比较
- `ThisPC` 改为 `static` 属性，返回 `L.Text("ThisPCLabel")`
- `ParsePathSegments` 中面包屑路径用 sentinel、显示文本用 `ThisPC`

## 改动文件

### `src/SpaceMonger.App/Views/RecommendationsPanel.xaml`
- CleanUpButton / AnalyzeButton：`ToolTip` 从 `Style.Setter` 移到 Button 直接属性

### `src/SpaceMonger.App/MainWindow.xaml`
- ScanButton: `Padding` 从 `8,5` 改为 `16,5`（与分析/清理按钮对齐）

### `src/SpaceMonger.App/MainWindow.xaml.cs`
- 新增 `ThisPCSentinel` 常量 + `ThisPC` 改为 `static` 属性
- `ParsePathSegments` / `BreadcrumbSegment_Click` / `BreadcrumbDropdown_Opened` 中的比较全部改用 sentinel

### `src/SpaceMonger.App/Localization/Strings.resx`
- 新增 `ThisPCLabel` = "This PC"

### `src/SpaceMonger.App/Localization/Strings.zh-CN.resx`
- 新增 `ThisPCLabel` = "此电脑"

## 打包
- `outputs\SpaceMonger-win-x64-folder-20260619-160117\SpaceMonger.App.exe`