# 迭代纪要：Computer Use 授权态核验与 APP 自测通道（2026-06-19）

## 背景

用户指出昨天 `docs/iteration-computer-use-coordinate-injection-2026-06-18.md:87` 附近把未跑通的经验写成总结不合格。本轮目标不是继续猜，而是把 PC use / Computer Use 赋能下的 SpaceMonger APP 自测链路实际跑通，并把仍未验证的点明确标注为待验证。

## Computer Use 源码与授权态核验

本轮读取了本地插件入口：

```text
C:\Users\BianShanghai\.codex\plugins\cache\openai-bundled\computer-use\26.616.30709\scripts\computer-use-client.mjs
```

源码关键信号：

- Windows 路径通过 `setupComputerUseRuntime()` 创建 `NativePipeComputerUseClient`，连接 `nodeRepl.nativePipe`。
- pipe 路径来自 `nodeRepl.env.SKY_CUA_NATIVE_PIPE_DIRECTORY`。
- app 授权弹窗由 native pipe 请求 `requestComputerUseApproval`，再调用 trusted `nodeRepl.createElicitation(...)`。
- 脚本没有直接读取 `OPENAI_API_KEY`，也没有在这个 JS 层直接判断账号登录状态。

因此需要分开看两条线：

1. **账号态/授权态线**：Computer Use 是否要求 Codex Desktop 的 OpenAI 账号登录/可信 `node_repl`，仍需从 Codex Desktop / native pipe 来源继续验证，不能用 API key 环境变量直接判定。
2. **Windows 权限线**：本轮实测 `sky.list_apps()`、`activate_window()`、`get_window_state()` 能用，说明基础 native pipe/授权至少可到枚举与截图；但输入注入仍被 Windows 权限/完整性级别阻断。

## 本轮实测结论

当前 shell 是 `Medium Mandatory Level`，而 SpaceMonger manifest 使用：

```xml
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```

实测现象：

- `CODEX_CUA_CURSOR_FORCE_WARP=true` 已进入当前环境。
- Computer Use 可以枚举/截图 `SpaceMonger Next`。
- `sky.click()` 后真实 `GetCursorPos` 不移动。
- 同一 shell 内原生 `SetCursorPos(2113,314)` 也不移动。
- 对 elevated SpaceMonger 窗口，UI Automation 只能看到根窗口，`Children=0`。
- Elevated app 创建的 named pipe，medium 客户端连接时出现 `Access to the path is denied.`。

所以本轮不再把“点击无响应”归因到 WPF 控件；已验证阻塞至少包含 Windows UIPI/完整性级别隔离。账号态/授权态是独立待验证维度，不与 UIPI 结论混写。

## 已落地的 APP 自测通道

为实现“解放双手”的可重复自测，本轮新增了一个显式启用的本地验收通道：

- `src/SpaceMonger.App/Diagnostics/AcceptanceAutomationServer.cs`
- `src/SpaceMonger.App/MainWindow.xaml.cs`
- `scripts/spacemonger-acceptance.ps1`
- `scripts/run-spacemonger-acceptance-smoke.ps1`

设计原则：

- 默认不启用；仅当 `SPACEMONGER_ACCEPTANCE_PIPE=true` 或 `SPACEMONGER_ACCEPTANCE_SERVER=true` 时启动。
- 监听 `127.0.0.1`，默认端口 `39187`，避免 elevated named pipe ACL 造成 medium 客户端无法连接。
- 命令在 WPF UI 线程内执行，绕过真实鼠标注入失败，但仍走真实 `MainWindow`、`TreemapViewModel`、导航栈和地址栏模式逻辑。
- 用于工程自测，不替代最终真实鼠标/视觉验收；Computer Use 输入注入恢复后仍应回到真实点击链路复验。

## 运行方式

启用验收 server：

```powershell
[Environment]::SetEnvironmentVariable('SPACEMONGER_ACCEPTANCE_PIPE','true','User')
[Environment]::SetEnvironmentVariable('SPACEMONGER_ACCEPTANCE_PORT','39187','User')
```

发布独立验收产物：

```powershell
dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Debug -o .\work\acceptance-tcp --no-restore
```

启动 app 后运行 smoke：

```powershell
Start-Process -FilePath .\work\acceptance-tcp\SpaceMonger.App.exe -WorkingDirectory .\work\acceptance-tcp
Start-Sleep -Seconds 5
.\scripts\run-spacemonger-acceptance-smoke.ps1
```

单步命令示例：

```powershell
.\scripts\spacemonger-acceptance.ps1 -Command state
.\scripts\spacemonger-acceptance.ps1 -Command navigate -Path D:\AppData\Visual Studio\Projects\spacemonger-next\work\ui-acceptance-sample\alpha
.\scripts\spacemonger-acceptance.ps1 -Command edit
.\scripts\spacemonger-acceptance.ps1 -Command blur
.\scripts\spacemonger-acceptance.ps1 -Command back
.\scripts\spacemonger-acceptance.ps1 -Command forward
.\scripts\spacemonger-acceptance.ps1 -Command up
```

## 已通过的 smoke 验证

本轮实际运行：

```powershell
.\scripts\run-spacemonger-acceptance-smoke.ps1
```

结果：

```json
{
  "ok": true,
  "checks": [
    { "name": "edit enters edit mode", "ok": true },
    { "name": "blur returns breadcrumb mode", "ok": true },
    { "name": "beta navigation enables back", "ok": true },
    { "name": "forward returns beta", "ok": true },
    { "name": "up returns alpha", "ok": true },
    { "name": "outside navigation updates selected path", "ok": true }
  ]
}
```

这次是真跑通，不再写“待验证成功经验”。

## 注意事项

- 如果发布目录被已运行 app 锁住，会出现 `MSB3021` / `MSB3027`，先关闭对应 `SpaceMonger.App.exe` 或换一个新 `-o` 输出目录。
- 当前还有旧 elevated `SpaceMonger.App.exe` 可能残留；非管理员 shell 无法读取其 `Path` / `CommandLine` 属于权限隔离现象，不代表文件缺失。
- `SPACEMONGER_ACCEPTANCE_PIPE` 名称目前为兼容旧命名保留，实际通道已改为 localhost TCP；后续可重命名为 `SPACEMONGER_ACCEPTANCE_SERVER` 并清理旧名。
- 自测通道是工程效率工具；真实鼠标、真实光标可见性、Computer Use 账号态/授权态仍需继续单独排查。
