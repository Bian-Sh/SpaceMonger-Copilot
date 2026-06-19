# 导航路径一致性鼠标级自测计划

## 目标

覆盖用户可能真实发生的导航路径操作，重点验证三类输入源不会互相污染：

1. 地址栏手动输入/修改路径。
2. 地址栏右侧 folder picker 选择路径。
3. TreeView / Treemap / 面包屑 / 前进后退上级导航得到的路径。

核心验收结论只有一个：点击 Scan / 按 F5 时，实际扫描目标必须等于导航栏当前显示并由 `SelectedPath` 记录的路径，不能扫描历史阶段出现过的其他路径。

## 自测原则

- 鼠标交互必须真实点击：地址栏点击、folder picker 点击、TreeView/Treemap/面包屑点击、Scan 按钮点击、F5 前置聚焦都走真实鼠标/键盘。
- acceptance TCP 只用于准备目录、启动扫描、读取状态或辅助定位窗口；不能替代用户交互作为通过依据。
- 每个用例至少记录三项状态：截图中的地址栏路径、acceptance `SelectedPath`、acceptance `CurrentRootPath`。
- 扫描类用例还应验证扫描完成后的根路径；当前 acceptance state 未暴露 `CurrentSession.TargetPath`，建议补充后再做强断言。
- 不扫描整盘做主流程，优先使用小型测试目录，避免把扫描耗时误判为卡死。

## 建议测试数据

创建一个可控目录树：

```powershell
$base='C:\tmp\sm-nav-path-test'
$paths=@(
  "$base\A\A1\A1Leaf",
  "$base\A\A2",
  "$base\B\B1",
  "$base\Outside\O1",
  "$base\中文 空格\层级-1\leaf",
  "$base\LongParent\MiddleFolder\LeafFolder"
)
$paths | ForEach-Object { New-Item -ItemType Directory -Force -Path $_ | Out-Null }
1..5 | ForEach-Object { Set-Content -Path "$base\A\A1\A1Leaf\file$_.bin" -Value ('a' * (1024 * $_)) -Encoding ASCII }
1..3 | ForEach-Object { Set-Content -Path "$base\B\B1\b$_.log" -Value ('b' * (2048 * $_)) -Encoding ASCII }
Set-Content -Path "$base\中文 空格\层级-1\leaf\中文文件.txt" -Value '测试' -Encoding UTF8
```

推荐三个代表路径：

- P0：`C:\tmp\sm-nav-path-test`
- P1：`C:\tmp\sm-nav-path-test\A\A1\A1Leaf`
- P2：`C:\tmp\sm-nav-path-test\B\B1`
- P3：`C:\tmp\sm-nav-path-test\Outside\O1`
- P4：`C:\tmp\sm-nav-path-test\中文 空格\层级-1\leaf`

## 环境准备

1. 使用最新 Release folder publish 启动：`outputs\SpaceMonger-win-x64-folder-20260619-140554\SpaceMonger.App.exe`。
2. 启用 acceptance server：`$env:SPACEMONGER_ACCEPTANCE_SERVER='1'`。
3. 将窗口置前并截图，确认地址栏、Scan 按钮、folder picker、Treemap 区域可见。
4. 若使用 Computer Use，先确认真实鼠标点击会移动系统光标；若使用 acceptance `click_coord`，需确认返回 `moved:true`。

## 通用通过标准

每次路径变化后：

- 地址栏截图显示的路径等于预期路径。
- `SelectedPath` 等于预期路径。
- 如果预期路径在已扫描树内，`CurrentRootPath` 等于预期路径或扫描根；如果预期路径在扫描树外，`IsExternalRoot=true`。
- 点击 Scan 或按 F5 后，扫描目标等于点击前的 `SelectedPath`。
- 扫描完成后，地址栏和 `SelectedPath` 不回跳到历史路径。

## P0 基础路径源用例

| ID | 场景 | 鼠标级步骤 | 期望 |
| --- | --- | --- | --- |
| NAV-001 | folder picker 后立即 Scan | 点击 folder picker → 在系统选择器选 P1 → 点击 Scan | `SelectedPath=P1`，实际扫描 P1，不扫描之前路径 |
| NAV-002 | 手动输入后立即 Scan | 点击地址栏 → 全选输入 P2 → Enter → 点击 Scan | `SelectedPath=P2`，实际扫描 P2 |
| NAV-003 | Tree/Treemap 下钻后 Scan | 先扫描 P0 → 鼠标双击/点击下钻到 P1 → 点击 Scan | `SelectedPath=P1`，实际扫描 P1 |
| NAV-004 | 面包屑点击父级后 Scan | 位于 P1 → 点击面包屑 `A1` → 点击 Scan | `SelectedPath=...\A\A1`，实际扫描该父级 |
| NAV-005 | 上级按钮后 Scan | 位于 P1 → 点击导航栏上级按钮 → 点击 Scan | `SelectedPath` 同步为父级，扫描父级 |
| NAV-006 | 后退按钮后 Scan | P0→P1→P2 导航后点击 Back → 点击 Scan | 扫描 Back 后地址栏显示的路径 |
| NAV-007 | 前进按钮后 Scan | NAV-006 后点击 Forward → 点击 Scan | 扫描 Forward 后地址栏显示的路径 |
| NAV-008 | F5 扫描 | 地址栏显示 P2 → 鼠标点击空白区聚焦窗口 → 按 F5 | 实际扫描 P2 |

## P1 混合顺序回归用例

| ID | 场景 | 鼠标级步骤 | 期望 |
| --- | --- | --- | --- |
| MIX-001 | folder picker 覆盖旧 TreeView 路径 | 扫描 P0 → 下钻 P1 → folder picker 选 P2 → 点击 Scan | 扫描 P2，不扫描 P1 |
| MIX-002 | 手动输入覆盖旧 TreeView 路径 | 扫描 P0 → 下钻 P1 → 手动输入 P2 → 点击 Scan | 扫描 P2，不扫描 P1 |
| MIX-003 | TreeView 覆盖旧手动路径 | 手动输入 P2 但不扫描 → 导航回已扫描树内 P1 → 点击 Scan | 扫描 P1 |
| MIX-004 | folder picker 覆盖旧手动路径 | 手动输入 P1 → folder picker 选 P2 → 点击 Scan | 扫描 P2 |
| MIX-005 | 手动输入覆盖旧 folder picker 路径 | folder picker 选 P1 → 手动输入 P2 → 点击 Scan | 扫描 P2 |
| MIX-006 | 外部路径覆盖旧扫描树路径 | 扫描 P0 → 下钻 P1 → 手动输入 P3 → 点击 Scan | 扫描 P3，且不回跳 P1 |
| MIX-007 | 扫描树内路径覆盖外部路径 | 手动输入 P3 → 再手动输入 P1 或面包屑选 P1 → 点击 Scan | 扫描 P1 |
| MIX-008 | 连续快速改路径 | 手动输入 P1 → 不扫描 → 手动输入 P2 → 点击 Scan | 只扫描最后一次 P2 |

## P2 地址栏编辑态与点击穿透用例

| ID | 场景 | 鼠标级步骤 | 期望 |
| --- | --- | --- | --- |
| EDIT-001 | 编辑态 Enter 提交 | 点击地址栏 → 输入 P1 → Enter | 退出编辑态，`SelectedPath=P1` |
| EDIT-002 | 编辑态失焦不吞 Scan | 点击地址栏 → 输入 P2 → 点击 Scan | 若点击先导致提交/退出，最终扫描 P2；不能扫描旧路径 |
| EDIT-003 | 编辑态点击 folder picker | 点击地址栏进入编辑态 → 点击 folder picker 选 P1 | 选择器打开，最终 `SelectedPath=P1` |
| EDIT-004 | 编辑态点击 Treemap 退出 | 进入编辑态 → 点击 Treemap 空白/节点 → 再点 Scan | 不吞掉后续点击，扫描当前地址栏路径 |
| EDIT-005 | 编辑态点击标题栏退出 | 进入编辑态 → 点击标题栏 → 首次点击面包屑父级 | 首击面包屑有效，`SelectedPath` 同步父级 |
| EDIT-006 | Escape 取消编辑 | 地址栏改成 P2 但按 Escape | 预期行为需产品确认：保留旧路径或取消输入；但 Scan 必须与最终显示路径一致 |

## P3 边界路径用例

| ID | 场景 | 鼠标级步骤 | 期望 |
| --- | --- | --- | --- |
| EDGE-001 | 路径尾部反斜杠 | 手动输入 `P1\` → Scan | 规范化后扫描 P1，不进入错误外部节点 |
| EDGE-002 | 前后空格 | 输入 `  P1  ` → Scan | Trim 后扫描 P1 |
| EDGE-003 | 中文与空格路径 | folder picker 或手动输入 P4 → Scan | 中文显示正常，扫描 P4 |
| EDGE-004 | 不存在路径 | 输入不存在路径 → Scan | 不应扫描旧路径；应提示错误或保持可理解状态 |
| EDGE-005 | 盘符根目录 | 输入 `C:\` 或选择盘符根 → Scan | 扫描盘符根；上级按钮到边界后不可继续错误跳转 |
| EDGE-006 | UNC/网络路径 | 输入 `\\server\share`（如有环境） → Scan | 要么扫描该 UNC，要么明确提示不支持；不能扫描旧路径 |
| EDGE-007 | 权限不足路径 | 选择受限目录 → Scan | 错误归因到权限，不回退扫描历史路径 |
| EDGE-008 | 路径大小写变化 | 输入同一路径不同大小写 → Scan | 不重复异常入栈，扫描同一路径 |

## P4 扫描中交互用例

| ID | 场景 | 鼠标级步骤 | 期望 |
| --- | --- | --- | --- |
| RUN-001 | 扫描中改地址栏 | 点击 Scan 后立即尝试输入 P2 | UI 若允许修改，当前扫描不应偷换目标；下一次 Scan 才使用 P2 |
| RUN-002 | 扫描中点击 folder picker | 扫描中点击 folder picker | 不崩溃；当前扫描目标保持启动时路径 |
| RUN-003 | 扫描中点击 TreeView/Treemap | 扫描中点击旧节点/空白 | 不改变当前扫描目标 |
| RUN-004 | Cancel 后改路径再 Scan | 扫描 P0 → Cancel → 输入 P2 → Scan | 第二次扫描 P2 |
| RUN-005 | 连续点击 Scan | 快速双击 Scan | 只启动一次扫描；目标等于第一次点击时 `SelectedPath` |

## P5 视觉与历史导航一致性用例

| ID | 场景 | 鼠标级步骤 | 期望 |
| --- | --- | --- | --- |
| HIST-001 | 同一路径重复点击 | 多次点击当前面包屑段或当前路径 | 不重复入栈；Back 行为不出现原地循环 |
| HIST-002 | 外部路径再 Back | 扫描 P0 → 手动输入 P3 → Back | 返回进入外部路径前的路径，`SelectedPath` 同步 |
| HIST-003 | Back 后手动输入清空 Forward | P0→P1→P2 → Back → 输入 P3 | Forward 历史应清空或符合产品预期；Scan 扫 P3 |
| HIST-004 | 面包屑 dropdown 子级选择 | 点击尾部 `>` dropdown 选子目录 | 地址栏与 `SelectedPath` 同步，Scan 扫所选子级 |
| HIST-005 | folder picker 取消 | 打开 folder picker 后 Cancel | 路径保持打开前值，Scan 扫原路径 |

## 推荐执行顺序

1. 先执行 NAV-001～NAV-008，确认三类路径源单独正确。
2. 再执行 MIX-001～MIX-008，专门复现“历史路径污染”。
3. 然后执行 EDIT-001～EDIT-006，避免编辑态吞点击造成假阳性。
4. 最后执行 EDGE/RUN/HIST，覆盖边界和真实使用节奏。

## 建议补充的可观测性

为让鼠标级自测可以自动判定“实际扫描了哪个路径”，建议 acceptance state 后续增加：

- `IsScanning`：当前是否扫描中。
- `CurrentSessionTargetPath`：最后完成扫描的 `ScanSession.TargetPath`。
- `LastScanRequestedPath`：点击 Scan/F5 时捕获的请求路径。
- `LastScanCompletedPath`：扫描完成时的实际 session 路径。

有了这些字段后，每个用例都可以自动断言：

```text
BeforeScan.SelectedPath == LastScanRequestedPath == LastScanCompletedPath == ExpectedPath
```

## 失败记录模板

| 字段 | 内容 |
| --- | --- |
| 用例 ID | 例如 MIX-001 |
| 操作序列 | 真实鼠标/键盘步骤 |
| 点击前截图路径 | 地址栏显示路径 |
| 点击前 `SelectedPath` | acceptance state |
| 点击前 `CurrentRootPath` | acceptance state |
| Scan 请求路径 | 若已补字段，记录 `LastScanRequestedPath` |
| 扫描完成路径 | 若已补字段，记录 `LastScanCompletedPath` |
| 实际现象 | 是否扫描旧路径、是否回跳、是否吞点击 |
| 截图/日志 | 文件路径 |
