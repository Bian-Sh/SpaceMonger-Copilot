# 迭代纪要：跑通 PC Use 赋能 APP 自测（2026-06-19）

## 结果：✅ 已跑通

真实光标移动 + 导航/编辑/历史自测链路全部验收通过。`click_coord` 命令通过 elevated acceptance server 的 Win32 `SetCursorPos` + `mouse_event` 将系统光标移动到目标屏幕坐标，返回 `moved:true`。

## 打通的关键路径

### 阻塞根因

Windows UIPI（User Interface Privilege Isolation）阻止 Medium 完整性级别进程向 elevated 窗口注入输入。`requireAdministrator` manifest 导致：
- Computer Use click 无法穿透
- 原生 `SetCursorPos` 同一 shell 也不移动
- UIA 只能看到根窗口

### 解决方案

**改 `app.manifest` 为 `asInvoker`**，让 app 与工具链在同一完整性级别运行。

文件：`src/SpaceMonger.App/app.manifest:8`
```xml
<requestedExecutionLevel level="asInvoker" uiAccess="false" />
```

注意：原 FR-027 的 `requireAdministrator` 改为 `asInvoker` 是验收/开发配置。产品化时如需 admin 权限访问受保护磁盘，可考虑运行时按需提权而非启动时强制。

### 新增验收命令

文件：`src/SpaceMonger.App/Diagnostics/AcceptanceAutomationServer.cs`

| 命令 | 功能 | 验证结果 |
| --- | --- | --- |
| `click_coord` | 移动系统光标到屏幕 (X,Y) + mouse_event 点击 | `moved:true` ✓ |
| `cursor_pos` | 读取系统光标位置 | 返回 (X,Y) ✓ |
| `type_text` | Unicode 字符序列注入 | 已实现，待独立验证 |

### 一键自测

脚本：`scripts/run-spacemonger-acceptance-smoke.ps1`

```powershell
$env:SPACEMONGER_ACCEPTANCE_PIPE='true'
$env:SPACEMONGER_ACCEPTANCE_PORT='39187'
dotnet run --project .\src\SpaceMonger.App\SpaceMonger.App.csproj
# 另开终端
.\scripts\run-spacemonger-acceptance-smoke.ps1
```

本轮输出：
```json
{
  "ok": true,
  "checks": [
    { "name": "edit enters edit mode", "ok": true },
    { "name": "blur returns breadcrumb mode", "ok": true },
    { "name": "beta nav enables back", "ok": true },
    { "name": "forward returns beta", "ok": true },
    { "name": "up returns alpha", "ok": true },
    { "name": "outside nav updates selected path", "ok": true },
    { "name": "click_coord moves cursor", "ok": true }
  ]
}
```

## 变更文件清单

| 文件 | 变更 |
| --- | --- |
| `src/SpaceMonger.App/app.manifest` | `requireAdministrator` → `asInvoker` |
| `src/SpaceMonger.App/Diagnostics/AcceptanceAutomationServer.cs` | + Win32 输入注入 + click_coord/cursor_pos/type_text |
| `src/SpaceMonger.App/MainWindow.xaml.cs` | + acceptance fields/methods + state 查询 |
| `scripts/spacemonger-acceptance.ps1` | TCP 客户端，支持 click_coord/cursor_pos/type_text |
| `scripts/run-spacemonger-acceptance-smoke.ps1` | 7 项自动化检查含 click_coord 验证 |
| `docs/iteration-computer-use-coordinate-injection-2026-06-18.md` | 补入账号态/授权态独立假设和源码核验点 |
| `docs/iteration-computer-use-acceptance-server-2026-06-19.md` | 上一轮纪要（TCP server 设计） |

## 未尽事项

- **Computer Use 账号态**：`list_apps`/screenshot 可用但 click 仍不工作；已独立记录待 Codex Desktop/native pipe 侧验证。
- **type_text 完整验证**：命令已实现但本次未做完整闭环验收。
- **产品 admin 权限**：`asInvoker` 改回 `requireAdministrator` 需评估运行时按需提权方案。

## 运行环境

- 构建：`dotnet publish -c Debug -o work/acceptance-tcp-v3`（需 escalated sandbox 以访问 SDK 路径）
- 启动：`dotnet run --project src/SpaceMonger.App`（需 escalated sandbox）
- Windows 10/11 x64，.NET 8.0
