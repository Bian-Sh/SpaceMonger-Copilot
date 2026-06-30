# Copilot 交互与 Toolcall 本轮变更 Checklist

生成时间：2026-06-27

## 1. UAC / 扫描启动
- [x] 扫描前不再动态 `runas` 请求 UAC。
- [x] 不再通过新开管理员实例接续扫描。
- [x] 移除 `--scan <path>` 自动接续入口。
- [x] 当前扫描仍通过当前实例的 `ScanCommand` 执行。

验证依据：
- `src/SpaceMonger.App/ViewModels/MainViewModel.cs` 不再包含 `ElevatedScan`、`LastScanDelegatedToElevatedInstance`、`LastScanElevationCancelled`。
- `src/SpaceMonger.App/App.xaml.cs` 不再包含 `--scan` 自动扫描入口。
- `src/SpaceMonger.App/MainWindow.Copilot.cs` 仍调用当前实例的 `mainVm.ScanCommand.Execute(null)`。
- `src/SpaceMonger.App/Services/UpdateService.cs` 仍包含 `runas`，但它属于更新安装流程，不属于扫描流程。

## 2. Step 展示触发规则
- [x] 单纯扫描指令，例如“请扫描 G 盘”，不显示 step 浮层。
- [x] 多步骤自动计划，例如“扫描后继续清理分析/写推荐”，才显示 step 浮层。
- [x] 已新增单测覆盖单纯扫描不弹 step。

验证依据：
- `tests/SpaceMonger.App.Tests/ChatViewModelProposalTests.cs` 包含 `SendCommand_ForSafeScanOnlyIntent_AutoExecutesWithoutWorkflowStepPopup`。
- `src/SpaceMonger.App/ViewModels/ChatViewModel.cs` 的 `BuildWorkflowSteps` 对非多步骤计划返回空列表。

## 3. Step 浮层布局
- [x] step 浮层从消息列表顶部移到输入框上方。
- [x] 默认只显示底部小 pill：`第 X/Y 步`。
- [x] 鼠标悬停 pill 区域才展开步骤列表。
- [x] step 浮层不再覆盖用户消息。
- [x] step 浮层没有复制按钮。

验证依据：
- `src/SpaceMonger.App/Views/ChatPanel.xaml` 包含 `Workflow Step Indicator (above input, hover to expand)`。
- step 展示节点位于输入框区域之后、context indicator 之前，使用 `DockPanel.Dock="Bottom"`。
- step 浮层模板中没有 `CopyButton`、`CopyMessageToolTip`、`MouseLeftButtonDown="CopyButton_MouseLeftButtonDown"`。

## 4. Step 三态 icon
- [x] `idle`：统一尺寸空心圆。
- [x] `running`：统一尺寸加粗、未封口圆环，并旋转。
- [x] `finish`：统一尺寸圆圈内对勾。
- [x] 三态均为 `14x14` 矢量图标，不再依赖文本 glyph 字号。
- [x] 底部 pill 图标绑定当前步骤状态。

验证依据：
- `src/SpaceMonger.App/Views/ChatPanel.xaml` 使用 `Ellipse` + `Path` 绘制 step icon。
- `running` 路径包含旋转 `DoubleAnimation`。
- `finish` 路径包含圆圈和对勾路径。
- `src/SpaceMonger.App/ViewModels/ChatViewModel.cs` 暴露 `CurrentWorkflowIconState`。

## 5. Slash commands / 确认卡片交互
- [x] 输入 `/` 会显示 `/new` 与 `/clear`，并带 description。
- [x] `/clear` 直接清空当前聊天上下文。
- [x] confirmation card 移到输入框上方 overlay，不再放在消息气泡内。

验证依据：
- `tests/SpaceMonger.App.Tests/ChatViewModelProposalTests.cs` 覆盖 slash menu、`/clear`、confirmation card overlay。
- `src/SpaceMonger.App/Views/ChatPanel.xaml` 的消息内 interaction card 已保持 `Visibility="Collapsed"`。

## 6. Unity cleanup skill
- [x] 移除 skill 内本机绝对路径引用，例如 `E:\Unity\...`。
- [x] 保留有意义的便携路径，例如 `%APPDATA%\UnityHub\...`。
- [x] 增加 localization contract：step/user-facing 文案遵循 app 当前语言，本地化示例不能硬编码。
- [x] 参考 UnityLauncherPro 的项目识别逻辑：显式项目路径、`Assets/` + `ProjectSettings/`、`ProjectSettings/ProjectVersion.txt`。
- [x] Hub 项目归属必须来自 Hub 文件、显式历史或文件系统证据，不从注册表推断。

验证依据：
- `skills/unity-project-cleanup/SKILL.md` 包含 `Localization contract`。
- `skills/unity-project-cleanup/SKILL.md` 不包含 `system_prompts_leaks` 或 `E:\Unity`。
- `skills/unity-project-cleanup/SKILL.md` 包含 `%APPDATA%\UnityHub\projects-v1.json`、`projectDir.json`、`editors.json`、`secondaryInstallPath.json`。

## 7. Registry toolcall
- [x] 新增只读 toolcall：`read_unity_registry_context`。
- [x] 只读固定 allowlist，不允许 AI 任意读取注册表路径。
- [x] allowlist 包含 Unity Installer 与 Unity Hub uninstall 相关节点。
- [x] tool 输出明确 registry 只用于佐证 Unity/Hub/editor 安装环境，不推断 Hub 项目归属。
- [x] 已注册到 app 的 Agent tool DI。
- [x] 已补单测验证 allowlist。

验证依据：
- `src/SpaceMonger.Core/Services/Agent/UnityRegistryAgentTool.cs` 定义 `ReadUnityRegistryContextTool`。
- `src/SpaceMonger.App/App.xaml.cs` 注册 `ReadUnityRegistryContextTool`。
- `tests/SpaceMonger.Core.Tests/AgentProposalTests.cs` 覆盖 `ReadUnityRegistryContextTool_ReadsFixedUnityRegistryAllowlist`。

## 8. 文档与发布
- [x] 重写 `docs/2026-06-27-copilot-safe-auto-workflow.md`，避免乱码文档继续使用。
- [x] 维护 `task_plan.md`、`findings.md`、`progress.md` 记录本轮计划、发现和进展。
- [x] 已按 WPF 改动要求发布可运行包到 `outputs/`。

验证依据：
- 最新发布目录：`outputs/SpaceMonger-Copilot-2026-06-27-171656`。

## 9. 自动验证命令

待执行/已执行命令：

```powershell
dotnet test tests/SpaceMonger.App.Tests/SpaceMonger.App.Tests.csproj --no-restore --filter "ChatViewModelProposalTests"
dotnet test tests/SpaceMonger.Core.Tests/SpaceMonger.Core.Tests.csproj --no-restore --filter "AgentProposalTests|AiSkillRouterTests|AiInteractionCardTests"
dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj -c Debug --no-restore
rg -n -S "system_prompts_leaks|E:\\Unity|ElevatedScan|LastScanDelegated|LastScanElevation" skills src tests docs
rg -n -S -- "--scan" src/SpaceMonger.App
```

验证结果：已通过。

- [x] `ChatViewModelProposalTests`：通过，7 个测试全部通过。
- [x] `AgentProposalTests|AiSkillRouterTests|AiInteractionCardTests`：通过，30 个测试全部通过。
- [x] `dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj -c Debug --no-restore`：通过，0 错误；仍有既有 `NU1701` 包兼容警告。
- [x] `skills/unity-project-cleanup/SKILL.md` 不包含 `system_prompts_leaks` 或 `E:\Unity`。
- [x] `src/SpaceMonger.App` 不包含扫描 UAC 残留标记：`ElevatedScan`、`LastScanDelegated`、`LastScanElevation`、`--scan`。
- [x] 正向检查确认存在：`read_unity_registry_context`、`Workflow Step Indicator`、`CurrentWorkflowIconState`、单纯扫描不弹 step 的测试。

## 10. CUA 真实 UI 验证
- [x] 使用 CUA snapshot-before-action 流程启动并验证最新发布包：`outputs/SpaceMonger-Copilot-2026-06-27-171656/SpaceMonger.App.exe`。
- [x] 初始 snapshot 识别到真实窗口：`SpaceMonger Copilot`，并定位到聊天输入框 `InputTextBox` 与发送按钮。
- [x] 通过 CUA `set_value` 输入 `/` 后重新 snapshot，确认 slash menu 出现 `/new` 与 `/clear`，且两项都有 description。
- [x] 通过 CUA 输入 `请扫描 Z盘` 并点击发送，使用不存在的 `Z:` 避免扫描真实磁盘。
- [x] 发送后重新 snapshot，确认 UI 树中没有 `第 X/Y 步` step 浮层节点，符合“单纯扫描不弹 step”。
- [x] 关闭窗口时出现退出确认框；通过 CUA 点击“确认”后，验证进程退出。

CUA 实测记录：
- PID：`11340`
- Window title：`SpaceMonger Copilot`
- Window id：`8851418`
- `/` 菜单 snapshot：出现 `/new`、`/clear`、`新建会话，清空当前聊天上下文`、`清除当前对话，不影响扫描数据`。
- 单纯扫描 snapshot：出现 `Z:` 路径状态和聊天消息，但未出现 step 进度浮层。
## 10. 非法/不可用扫描目标阻断
- [x] AI 路由出 `StartScan` 后，会在自动执行前校验目标路径是否存在且可访问。
- [x] `Z:` / `Z:\` 这类不存在或未挂载盘符不会调用扫描 action。
- [x] 阻断时直接在聊天区提示“无法扫描 ... 不存在、未挂载，或当前不可访问”。
- [x] 阻断不会显示 step 浮层，也不会进入“待扫描”以外的扫描状态。

验证依据：
- `src/SpaceMonger.App/ViewModels/ChatViewModel.cs` 包含 `TryValidateAutomaticAction`、`NormalizeScanTarget`、`IsScanTargetAvailable`。
- `tests/SpaceMonger.App.Tests/ChatViewModelProposalTests.cs` 包含 `SendCommand_ForUnavailableScanTarget_ShowsErrorWithoutExecutingAction`，断言 action executor 不会被调用。
- `dotnet test tests/SpaceMonger.App.Tests/SpaceMonger.App.Tests.csproj --no-restore --filter ChatViewModelProposalTests` 通过：8/8。
- CUA 实测发布包 `outputs/SpaceMonger-Copilot-2026-06-27-174735`：输入 `请扫描 Z盘` 后，UIAutomation 读取到 `无法扫描 Z:\：该磁盘或文件夹不存在、未挂载，或当前不可访问。`，窗口仍显示 `待扫描` 且没有 step 浮层。

### 10.1 AI-first 修正
- [x] 不再由本地预处理直接生成非法盘符回复；不可用扫描目标会放行到模型层，由模型基于磁盘上下文回答。
- [x] 模型输入会附带 `Host disk context JSON`，包含 `requested_scan_target` 与 `available_drives`。
- [x] 磁盘扫描意图会强制请求 thinking 流，避免直接静态输出。
- [x] 本地仍保留最终安全闸门，防止模型或确认卡误触发不可访问路径扫描。

验证依据：
- `tests/SpaceMonger.App.Tests/ChatViewModelProposalTests.cs` 中 `SendCommand_ForUnavailableScanTarget_AsksModelWithDriveContextWithoutExecutingAction` 验证模型收到磁盘上下文且扫描 executor 未执行。
- `dotnet test tests/SpaceMonger.App.Tests/SpaceMonger.App.Tests.csproj --no-restore --filter ChatViewModelProposalTests` 通过：8/8。
- CUA 实测发布包 `outputs/SpaceMonger-Copilot-2026-06-27-180341`：模型根据可用驱动器列表回答 `Z盘 ... 不在当前可用的驱动器列表中`，状态保持 `待扫描`，未出现 step 浮层。
- 最终发布包：`outputs/SpaceMonger-Copilot-2026-06-27-180546`。
