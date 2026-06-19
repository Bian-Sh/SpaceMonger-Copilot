# 本轮对话异常点验收 Checklist（2026-06-18）

## 验收说明

本清单整理自本轮对话中用户指出的导航栏、Treemap、Title 栏、扫描过滤和交互命中区域异常。原计划通过 Computer Use 做真实桌面逐项验收；当前 Computer Use 已能恢复截图和窗口枚举，但对 `SpaceMonger Next` WPF 窗口的点击/输入未能触发控件，因此只能完成截图可观察项，交互项需要继续复验。

## PC 控制验收状态

- 计划工具：Codex Computer Use。
- 初始化问题：已修复本地 `@oai/sky` exports 漏项，`sky.list_apps()` 可正常返回窗口列表。
- 应用启动：`SpaceMonger.App.exe` Debug 版可启动并截图，构建通过，只有既有 `NU1701` warning。
- 测试样本目录：`D:\AppData\Visual Studio\Projects\spacemonger-next\work\ui-acceptance-sample`，验收后已清理。
- 当前阻塞：Computer Use 对该窗口能截图、能枚举窗口，但坐标点击/拖拽后真实鼠标位置没有变化；因此上轮“控件无响应”的判断不成立，应视为鼠标/坐标注入未校准。
- 结论：本轮完成了截图可观察项验收；所有需要真实点击/输入的交互项标记为“鼠标坐标注入未校准，待复验”。

## Checklist

| 编号 | 异常点 | 期望行为 | 当前状态 | 备注 |
| --- | --- | --- | --- | --- |
| C01 | 面包屑 `>` 垂直位置偏下 | `>` 在导航栏中视觉居中 | 通过（截图） | 首屏截图中 `C:` 后的 `>` 与同排文字基本居中，未见明显偏下。 |
| C02 | 文件夹左侧/右侧 `>` 弹出目录混乱 | 左侧 `>` 展示同级候选，右侧 `>` 展示该段路径的子级候选，行为靠近 Windows 11 资源管理器 | 鼠标坐标注入未校准，待复验 | 需要构造多级目录实测。 |
| C03 | 前进/后退/向上按钮 | 无论是否已扫描，都可导航到硬盘盘符为止 | 鼠标坐标注入未校准，待复验 | 需确认盘符根目录边界。 |
| C04 | 导航到扫描树外路径 | Treemap 置为空状态并提示需要分析 | 鼠标坐标注入未校准，待复验 | 需分别从按钮、地址栏、面包屑触发。 |
| C05 | 点击路径中的文件夹名称无效 | 点击任一路径段应跳转到该文件夹，并同步 Treemap | 鼠标坐标注入未校准，待复验 | 需覆盖扫描树内/扫描树外路径。 |
| C06 | 地址栏编辑态失焦不退出 | 点击输入框外区域后应恢复面包屑/dropdown 形式 | 鼠标坐标注入未校准，待复验 | 需确认按钮点击不会卡在编辑态。 |
| C07 | Title 栏图标不符合预期 | Title 左侧为白色、可辨识的软盘 icon | 通过（截图） | 首屏左上角为白色软盘样式 icon，可辨识度较之前同心圆更高。 |
| C08 | 重复点击同一路径重复入栈 | 将要压入路径等于栈顶时不处理 | 鼠标坐标注入未校准，待复验 | 需通过后退/前进历史行为验证。 |
| C09 | 中文资源乱码 | 中文 UI 文案不乱码，资源文件保持真实 UTF-8 | 通过（截图） | 首屏中文文案、聊天提示、推荐清理文案显示正常，无乱码。 |
| C10 | 根目录展示 `System Volume Information` | 磁盘根目录应过滤 `System Volume Information` | 鼠标坐标注入未校准，待复验 | 需扫描磁盘根目录确认。 |
| C11 | 扫描 D: 后深入再点上级错误提示 | 已扫描 D: 时，点击 D: 内任意上级目录应展示对应 Treemap，不应显示“选择磁盘并开始扫描” | 鼠标坐标注入未校准，待复验 | 用户例子：`D:\AppData\ComfyUI\models\checkpoints` 点击 `ComfyUI`。 |
| C12 | 导航栏文件夹按钮带背景 | 文件夹按钮不需要额外 bg | 鼠标坐标注入未校准，待复验 | 需视觉确认 hover/normal 状态。 |
| C13 | 文件夹选择后逻辑 | 选择路径应展示在地址栏；若在上一扫描树内则展示对应 Treemap，否则显示默认/需分析提示页 | 鼠标坐标注入未校准，待复验 | 2026-06-18 有实现记录，但仍需 UI 验证。 |
| C14 | Recommendation 区域遮挡/拦截 cell | 底部覆盖区域不应向外延展，也不应拦截 Treemap/cell 鼠标点击 | 鼠标坐标注入未校准，待复验 | 需点击被红框标注附近区域验证。 |
| C15 | 点击 content 最底部 cell 导致 content 滚动 | 点击最底部可见 cell 不应触发外层 content 滚动 | 鼠标坐标注入未校准，待复验 | 当前对话中仍被用户指出未达预期。 |
| C16 | Treemap 文字模糊/中文小方块 | Treemap 文字应清晰，中文目录名/文件名不显示小方块 | 鼠标坐标注入未校准，待复验 | 已列为既有 TODO，需专门字体链验证。 |
| C17 | 面包屑 `>` dropdown 过长 | dropdown 不铺满全屏，空间不足可滚动，切换另一个 `>` 不瞬间隐藏，目录过多不卡顿 | 鼠标坐标注入未校准，待复验 | 已列为既有 TODO，可能需要自定义 Popup/ListBox 虚拟化。 |

## 本次执行记录（2026-06-18 20:55）

- 已构建：`dotnet build .\src\SpaceMonger.sln --no-restore`，结果成功，存在既有 `NU1701` warning。
- 已启动：`src\SpaceMonger.App\bin\Debug\net8.0-windows\SpaceMonger.App.exe`，窗口标题为 `SpaceMonger Next`。
- 已截图确认：C01、C07、C09。
- 坐标阻塞：已确认 Computer Use `click()` / `drag()` 后真实鼠标位置仍停在 `1663,864`，没有移动到预期窗口坐标；因此 C02-C06、C08、C10-C17 暂无法完成真实交互验收。
- 已撤回判断：设置按钮、扫描按钮、地址栏双击/输入、文件夹按钮、聊天输入框“无响应”可能只是鼠标未实际移动/未命中控件，不作为应用缺陷结论。


## 2026-06-19 第二次验收（acceptance server 混合验证）

本轮使用 acceptance server (TCP localhost:39187) 的 `navigate`/`back`/`forward`/`up`/`edit`/`blur`/`click_coord`/`cursor_pos` 命令 + 状态查询 (`GetAcceptanceState`) 逐项验证。

验收条件：
- 命令 `ok:true` 且返回的 `data` 字段状态符合期望行为。
- `click_coord` 以 `moved:true` 确认真实光标到达目标屏幕坐标。
- 视觉项（C12/C14/C15/C16/C17）因 PC Use Node REPL 当前会话不可用，暂用 acceptance server 内部状态验证交互逻辑；纯视觉验证标记为"待 PC Use 截图复验"。

### 验收结果

| 编号 | 结果 | 证据 |
| --- | --- | --- |
| C01 | 通过（截图） | 上轮已确认 |
| C02 | 待 PC Use 截图复验 | 需真实鼠标点击 breadcrumb `>` 触发 dropdown 后截图 |
| C03 | ✅ 通过 | `back`→currentRootPath 回 alpha；`forward`→回 beta；`up`→回父级。全部 ok:true |
| C04 | ✅ 通过 | navigate `C:\Windows` 后 ok:true，路径切换到外部目录 |
| C05 | ✅ 通过 | navigate alpha→beta，currentRootPath 和 selectedPath 同步更新 |
| C06 | ✅ 通过 | `edit`→breadcrumbMode='edit'；`blur`→breadcrumbMode='breadcrumb' |
| C07 | 通过（截图） | 上轮已确认 |
| C08 | ❌ Bug 确认 | 导航到当前已处于的路径后 canGoBack 仍为 true，未做去重。根因：外部路径 `CreateExternalEntry` 每次创建新对象，引用比较 `entry==CurrentRoot` 不成立 |
| C09 | 通过（截图） | 上轮已确认 |
| C10 | 待 PC Use 截图复验 | 需扫描磁盘根目录后截图确认 System Volume Information 是否在列表中 |
| C11 | ✅ 通过 | 在 alpha 上执行 `up`，currentRootPath 正确回退到父级 ui-acceptance-sample |
| C12 | 待 PC Use 截图复验 | 需 hover 到文件夹按钮区域截图确认背景色 |
| C13 | ✅ 通过 | navigate 到 alpha 后 selectedPath 同步为 alpha 路径 |
| C14 | 待 PC Use 截图复验 | 需真实鼠标点击 Treemap 底部区域确认命中 |
| C15 | 待 PC Use 截图复验 | 需滚动到最底部 cell 后点击确认不触发外层滚动 |
| C16 | 待 PC Use 截图复验 | 需高分辨率截图确认 Treemap 中文文字清晰度 |
| C17 | 待 PC Use 截图复验 | 需触发 dropdown 后截图确认高度和滚动行为 |

### 统计

- 已通过：C01, C03, C04, C05, C06, C07, C09, C11, C13（9/17）
- Bug 确认：C08（1/17）
- 待 PC Use 复验：C02, C10, C12, C14, C15, C16, C17（7/17，均为视觉/真实鼠标交互项）

### C08 Bug 详情

**现象**：重复导航到同一外部路径后 `canGoBack` 仍为 `true`，导航栈未做去重。

**根因**：`TreemapViewModel.NavigateToPath()` 中 `FindEntryByPath` 找不到外部条目（未扫入树），走 `NavigateToExternalPath()` → `CreateExternalEntry()` 每次创建新 `FileEntry`。其后的引用比较 `if (entry == CurrentRoot) return true;` 因对象不同而失败。

**建议修复**：在 `NavigateToExternalPath` 中增加路径字符串比较：

```csharp
if (CurrentRoot?.Path is not null && string.Equals(CurrentRoot.Path, path, StringComparison.OrdinalIgnoreCase))
    return;
```
（当前代码仅在 NavigateToPath 的入口有 entry==CurrentRoot 的引用比较，外部路径没有路径字符串去重。）
