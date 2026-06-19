# TODO：本轮 UI 异常验收发现（2026-06-18）

## 验收工具问题

### T01 Codex Computer Use 初始化失败（已处理）

- 现象：初次尝试初始化 Windows PC 控制时连续失败，无法执行真实桌面逐项验收。
- 错误摘要：`Package subpath './dist/project/cua/sky_js/src/targets/windows/internal/computer_use_client_base.js' is not defined by "exports" ... @oai/sky/package.json`。
- 根因：本地 `@oai/sky` runtime 的 `package.json` 只导出了根入口，插件脚本需要导入 `@oai/sky/dist/.../computer_use_client_base.js`，被 Node package exports 拦截。
- 处理：已备份并为 `@oai/sky/package.json` 增加 `"./dist/*": "./dist/*"`，备份文件为 `package.json.codex-backup-20260618203511`。
- 验证：`sky.list_apps()` 已成功返回当前 Windows 应用窗口列表。

### T07 Computer Use 鼠标坐标注入未校准，导致 SpaceMonger 交互验收无效

- 操作要求：处理完 Computer Use 鼠标注入后，必须确认真实系统鼠标光标重新显示；不得让 helper 的 cursor suppress/overlay 状态残留。

- 发现时间：2026-06-18 20:55。
- 现象：`sky.list_apps()`、`activate_window()`、`get_window_state()` 和截图均可用，但对 `SpaceMonger Next` 调用 `click()` / `drag()` 后真实鼠标位置没有变化，说明之前点击没有实际落到控件上。
- 已校准：窗口真实矩形 `1120,296,2320,1096`，截图 origin `1120,296`；点击截图坐标 `993,18` 后真实鼠标仍为 `1663,864`，未移动到预期 `2113,314`。
- 影响：`docs/checklist-conversation-ui-acceptance-2026-06-18.md` 中需要真实点击/输入的 C02-C06、C08、C10-C17 暂无法验收。
- 当前定位：截图 `originX/originY` 与窗口矩形一致，问题不在 SpaceMonger/WPF；`sky.click()`/`sky.drag()` 未 warp 真实系统光标。已设置用户级 `CODEX_CUA_CURSOR_FORCE_WARP=true`，需重启 Codex 后复验 `GetCursorPos`。详见 `docs/iteration-computer-use-coordinate-injection-2026-06-18.md`。

## 需要优先复验/修复的不符合预期项

### T02 文件夹选择后的导航联动需要真实 UI 复验

- 对应 checklist：C13。
- 背景：`docs/iteration-folder-selection-navigation-sync-2026-06-18.md` 记录已实现，但本轮未能用 PC 控制验证。
- 验收标准：选择扫描树内路径时地址栏更新且 Treemap 跳转；选择扫描树外路径时地址栏更新且 Treemap 显示需分析/默认提示；再次扫描目标应使用当前显式选择路径。

### T03 Treemap 文字清晰度和中文 fallback

- 对应 checklist：C16。
- 背景：用户明确反馈 Treemap 文字模糊、不犀利，中文显示小方块。
- 验收标准：中文目录名在 Treemap cell 内正常显示；文字边缘清晰；不同 DPI 缩放下不退化。
- 建议方向：优先验证 SkiaSharp 字体 fallback、`SKTypeface` 选择、DPI 缩放、`SKPaint` hinting/subpixel 行为，不要复用已撤回的临时方案。

### T04 面包屑 `>` dropdown 长列表交互

- 对应 checklist：C17。
- 背景：用户明确反馈目录过多时 dropdown 铺满全屏、不可滚动、内容多会卡，切换另一个 `>` 时会瞬间隐藏。
- 验收标准：长列表有最大高度和滚动；不铺满全屏；切换不同 `>` 稳定；大量目录不卡顿。
- 建议方向：评估自定义 `Popup` + 虚拟化 `ListBox`，不要继续堆叠已撤回的 `ContextMenu/MenuItem` 临时方案。

### T05 最底部 Treemap cell 点击导致 content 滚动

- 对应 checklist：C15。
- 背景：用户明确反馈点击 content 可见最下面的 cell 仍会变成 content 滚动。
- 验收标准：点击最底部可见 Treemap cell 时只触发 Treemap 交互，不触发外层内容区域滚动或焦点滚入。
- 建议方向：检查 `ScrollViewer`、`RequestBringIntoView`、Treemap 控件鼠标事件处理和外层容器焦点行为。

### T06 Recommendation 底部区域遮挡/拦截需复验

- 对应 checklist：C14。
- 背景：已提交 footer hit-test 修复，但本轮未能真实点击验证。
- 验收标准：红框区域不再向外遮挡更多 cell；即使无视觉遮挡，也不拦截底层 Treemap/cell 点击。

## 记录文件

- 完整验收清单：`docs/checklist-conversation-ui-acceptance-2026-06-18.md`
- 既有导航/Treemap 后续 TODO：`docs/todo-navigation-treemap-followups-2026-06-17.md`




