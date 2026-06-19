# 迭代纪要：Codex Computer Use 坐标注入诊断（2026-06-18）

## 背景

本轮只诊断 Codex Computer Use 鼠标坐标注入问题，不修改 SpaceMonger 应用功能代码。

已知现象：

- `sky.list_apps()`、`sky.activate_window()`、`sky.get_window_state()` 和 Windows.Graphics.Capture 截图可用。
- SpaceMonger 窗口截图 origin 与真实窗口矩形一致，例如 `originX=1120/originY=296/width=1200/height=800`，窗口真实矩形 `1120,296,2320,1096`。
- 调用 `sky.click({ x=993, y=18, screenshotId })` 后，预期屏幕坐标应为 `origin + point`，即 `2113,314`，但 `GetCursorPos` 仍停在原位置。
- `sky.drag()` 后真实鼠标位置同样没有变化。

## 本轮诊断结论

问题不应归因于 WPF 控件无响应，也不是 SpaceMonger UI 逻辑问题。当前阻塞发生在 Codex Computer Use 的鼠标注入/真实光标 warp 路径。

关键证据：

1. `get_window_state()` 返回的截图坐标是可信的：`originX/originY/width/height` 与 `GetWindowRect` 读取的窗口矩形一致。
2. `sky.click()` / `sky.drag()` 请求没有报错，`diagnostic_state` 中 `lastWindowIdentity` 的 `rootHwnd/inputHwnd/processId/title/bounds` 均指向 `SpaceMonger Next`。
3. 高频 `GetCursorPos` 采样覆盖 `sky.click()` 执行窗口，真实光标位置全程不变，没有出现“移动到目标后回弹”。
4. 二进制 `codex-computer-use.exe` 内存在 `CODEX_CUA_CURSOR_FORCE_WARP` 字符串，说明 runtime 支持强制 warp 系统光标的环境开关。
5. 当前 Codex / node_repl 会话的 `nodeRepl.env` 不可变，无法在本进程内热注入该环境变量；需要让 Codex app / helper 在启动前继承环境变量。

## 已执行修复

已写入用户级环境变量：

```powershell
[Environment]::SetEnvironmentVariable('CODEX_CUA_CURSOR_FORCE_WARP', 'true', 'User')
```

当前进程不会自动继承新用户环境变量，需要重启 Codex 桌面应用（或至少让 Codex 主进程和 `codex-computer-use.exe` helper 全部重新启动）。

## 重启后验证步骤

重启 Codex 后，先执行以下验证，不要先回到应用功能验收：

1. 确认环境变量：

```powershell
[Environment]::GetEnvironmentVariable('CODEX_CUA_CURSOR_FORCE_WARP', 'User')
```

期望输出：`true`。

2. 启动/激活 SpaceMonger，并获取窗口状态：

```javascript
const apps = await sky.list_apps();
const app = apps.find(a => JSON.stringify(a).includes('SpaceMonger'));
const win = app.windows.find(w => (w.title || '').includes('SpaceMonger'));
await sky.activate_window({ window: win });
const state = await sky.get_window_state({ window: win, include_screenshot: true, include_text: false });
const shot = state.screenshots[0];
```

3. 在 `sky.click()` 前后用 Win32 `GetCursorPos` 校验真实光标：

```javascript
const before = await getCursorPos();
await sky.click({ window: state.window, screenshotId: shot.id, x: 993, y: 18 });
await new Promise(resolve => setTimeout(resolve, 500));
const after = await getCursorPos();
console.log({ before, expected: { x: shot.originX + 993, y: shot.originY + 18 }, after });
```

通过标准：`after` 应接近 `expected`，而不是停在 click 前的位置。

4. 再验证 drag：

```javascript
await sky.drag({ window: state.window, screenshotId: shot.id, from_x: 100, from_y: 100, to_x: 300, to_y: 120 });
```

通过标准：`GetCursorPos` 最终位置应接近 `shot.originX + 300, shot.originY + 120`。

5. 只有 click/drag 的真实鼠标位置通过后，才回到 `docs/checklist-conversation-ui-acceptance-2026-06-18.md` 中 C02-C06、C08、C10-C17 的 SpaceMonger UI 交互验收。

## 后续注意

- 无论 click/drag 是否最终验证通过，处理结束前都必须确认真实系统鼠标光标可见；如果 Computer Use helper 仍在 suppress/overlay cursor，需要先释放或重启 helper，不能把光标留在不可见状态。

- 2026-06-19 复盘补充：使用官方 PC Use / Computer Use 后，系统默认光标可能持续不可见；不能只依赖 `ShowCursor(true)`，因为它可能只影响当前线程的显示计数。结束官方 PC Use 后必须显式恢复系统光标方案，并确认用户肉眼可见。

- 光标不可见时已验证的恢复动作：先 `taskkill /F /IM codex-computer-use.exe /T` 停止官方覆盖层；再调用 Win32 `SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE)` 重新加载系统光标；随后多次 `ShowCursor(true)`，用 `SetCursorPos` 和 `mouse_event(MOUSEEVENTF_MOVE)` 触发真实鼠标移动，最后用 `GetCursorPos` 确认位置。

- 重要纪律：每次官方 PC Use 之后都要主动恢复/显示真实光标，不能等用户提醒；如果用户说光标仍不可见，继续升级到 `SPI_SETCURSORS` 级别恢复，而不是重复普通 `ShowCursor(true)`。

- 2026-06-19 用户补充的硬规则：授权后必须执行完整序列“延迟 → 恢复/显示真实光标 → 将光标移动回被测应用窗口或将要操作的目标点 → 恢复焦点/置前被测窗口 → 再截图/点击”。只恢复焦点但不移动真实光标仍然不合格，因为截图和点击都会继续受 Codex/授权弹窗留下的光标位置噪音影响。

- 如果重启 Codex 后仍不移动真实鼠标，继续优先排查 Computer Use helper 的环境变量是否被传入 `codex-computer-use.exe`，以及 helper 是否仍走 overlay cursor 而非 system cursor warp。

- 不要把“点击后控件无响应”直接归因于 WPF / SpaceMonger，必须先用 `GetCursorPos` 证明真实鼠标确实到达了预期屏幕坐标。

- 带鱼屏/多显示器环境下，坐标映射以 `screenshot.originX/originY + screenshot x/y` 为准；当前证据显示 origin 映射本身是正确的。

- 如果需要验证是否需要管理员权限运行时，可以用终端调用 run as ，用户会给你bypass  系统弹窗。

## 2026-06-18 重启后复测补充

重启 Codex 后确认 `SKY_CUA_CURSOR_FORCE_WARP=true` 已进入进程环境，但 `sky.click()` 后 `GetCursorPos` 仍未移动。重新扫描 `codex-computer-use.exe` 字符串后确认实际开关名是 `CODEX_CUA_CURSOR_FORCE_WARP`，不是 `SKY_CUA_CURSOR_FORCE_WARP`。

已写入用户级环境变量：

```powershell
[Environment]::SetEnvironmentVariable('CODEX_CUA_CURSOR_FORCE_WARP', 'true', 'User')
```

当前 Codex 进程尚未继承该正确变量，需要再次重启 Codex 后复验。复验前先确认：

```powershell
$env:CODEX_CUA_CURSOR_FORCE_WARP
```

期望输出：`true`。

本次收尾已停止 `codex-computer-use.exe` helper，并确认 `ShowCursor(true)` 计数非负，避免光标不可见状态残留。

## 2026-06-18 管理员重启后复测补充

用户反馈已重启 Codex，并在系统弹窗中放行 app。当前 shell 复测结果仍显示：

- `$env:CODEX_CUA_CURSOR_FORCE_WARP=true` 已进入当前进程环境。
- `sky.click()` 对 SpaceMonger 的截图坐标 `x=993,y=18`，预期屏幕坐标 `2113,314`，但 `GetCursorPos` 从 `1165,824` 到 `1165,824`，未移动。
- 当前执行链路检测 `IsAdmin=false`，说明 Codex 工具子进程并未以管理员 token 运行，尽管外层 app 可能以管理员身份启动或系统弹窗已放行。
- 同一进程内原生 `SetCursorPos(2113,314)` 返回 `false` 且位置不变；`SendInput` 返回 `sent=1` 但位置不变。因此阻塞仍在 Windows 输入权限/进程 token/安全拦截层，不是 SpaceMonger，也不是截图 origin 映射。
- 本次收尾已停止 `codex-computer-use.exe` helper，并运行 `RestoreSystemCursor.exe` 恢复系统光标，避免光标不可见残留。

下一步应优先确认 Codex CLI/工具子进程本身是否以管理员 token 运行，而不只看 Codex 外壳窗口；若仍为非管理员，需要用管理员 PowerShell 启动 Codex，或排查 Codex desktop 对子进程降权/隔离的行为。

## 2026-06-19 账号态/授权态补充假设

用户补充：需要把“Computer Use 可能要求 OpenAI 账号登录/授权态，而 API key 形式可能无法完整启用”作为独立排查维度记录下来，不能只从鼠标坐标或 WPF 控件层面下结论。

当前源码核验点：

- 本地插件入口位于 `C:\Users\BianShanghai\.codex\plugins\cache\openai-bundled\computer-use\26.616.30709\scripts\computer-use-client.mjs`。
- 该脚本没有直接读取 `OPENAI_API_KEY` 或业务 API key；Windows 路径通过 `setupComputerUseRuntime()` 连接 `nodeRepl.nativePipe`，pipe 路径来自 `nodeRepl.env.SKY_CUA_NATIVE_PIPE_DIRECTORY`。
- app 授权弹窗由 native pipe 反向请求 `requestComputerUseApproval`，再调用 trusted `nodeRepl.createElicitation(...)` 完成；如果不在 trusted node_repl 中，会抛出 `Computer Use app approval UI is unavailable outside trusted node_repl`。
- 因此“账号态/授权态”和“API key”是否影响 Computer Use，需要继续从 Codex Desktop / trusted node_repl / native pipe 的权限与授权来源验证，而不能仅靠普通 shell 环境变量判断。

与本轮实测的关系：

- 本轮已确认 `sky.list_apps()`、`sky.activate_window()`、`sky.get_window_state()` 可用，说明 native pipe 和基础授权至少已经建立到可枚举/截图阶段。
- 本轮阻塞仍有一个已验证的 Windows 权限维度：当前工具子进程是 `Medium Mandatory Level`，而 SpaceMonger manifest 为 `requireAdministrator`；对 elevated WPF 窗口，Medium 进程读取不到完整 UIA 子树，且 `SetCursorPos` / `SendInput` 在当前环境下也未能移动真实光标。
- 后续排查必须把两条线分开：一条是 Computer Use 是否需要账号登录/授权态才能完整注入输入；另一条是被测 app 与工具链完整性级别不一致导致的 UIPI/输入隔离。
