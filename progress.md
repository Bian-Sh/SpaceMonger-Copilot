# Progress

- 已建立计划文件。
- 已确认 CodeGraph 索引可用且项目索引最新。
- 已参考 opencode：采用“prompt/skill/tool 声明能力，宿主只执行工具”的边界。
- 已将 `AiSkillRouter` 收敛为声明式 skill 注入：默认注入内置 skill prompt；显式 `@skill` 只收窄激活 skill；不再做中文/英文关键词意图枚举、不再生成动作卡、不再本地回答 FAQ。
- 已删除 `AgentRuntime` 的 host-side keyword heuristic tool prefetch；工具只能由模型显式 `tool_calls` 调用。
- 已让 `ChatViewModel` 给模型追加 Host disk context JSON（扫描状态、当前视图、选中项、已有推荐、可用磁盘、显式 proposal 上下文），让模型按 skill/tool 描述提议动作。
- 已移除 UI 主流程对 router `SuggestedAction`/`LocalAnswer` 的信任分支；动作进入 App 只能来自模型 proposal 生成的确认卡或用户确认卡执行。
- 已支持模型 proposal 创建 `DiscoverUnityLibraries` 确认卡。
- 验证：Core targeted tests 通过；App proposal tests 通过；dotnet MCP 方案构建成功且 Roslyn diagnostics 为空；cua 后台启动 WPF App 并成功读取 UIA 树（Chat 输入、Send、Scan、Recommendations 等控件可见）。

## 2026-06-29 21:01:28 +08:00
- 删除 AiSkillRoutingResult 中旧的 SuggestedAction/LocalAnswer 兼容字段；ChatViewModel 不再把主机路由建议动作写进 Host disk context。
- AgentRuntime 系统提示明确 SpaceMonger 是开放 skill-driven agent host，磁盘/MFT/注册表能力作为 tools 暴露，风险和流程由 skill + model reasoning 决定。
- 新增 AgentRuntime 回归测试：宿主不会根据自然语言关键词自动执行工具；只有模型显式输出 tool_calls 才执行工具循环。
- 验证：Core 目标测试 10 passed；App ChatViewModelProposalTests 11 passed；dotnet-debug-mcp build_solution 成功 0 errors；全量测试剩既有 RecommendationEngine Unity Library 推荐断言失败。
- CUA 后台验证：Start-Process 启动 WPF，UIA 找到 SpaceMonger Copilot、InputTextBox、Send；后台 set_value 写入 @unity-project-cleanup clean Unity Library 并重取快照确认，随后关闭测试进程。

## 2026-06-29 21:08:14 +08:00
- 继续推进 Agent host 化：将 Unity Library 自动推荐从宿主内置日期/Hub 风险算法改为 ReviewFirst 候选证据，风险降级/分级留给 skill + AI reasoning + 用户确认。
- 修复全量测试失败：RecommendationEngineTests.AnalyzeWithDiagnosticsAsync_AddsUnityLibraryRecommendationWhenProjectMarkersExist 现在覆盖旧日期仍不会被宿主判 Safe，并验证说明包含 skill/AI risk review。
- 验证：dotnet-debug-mcp build_solution 成功 0 errors；目标 Core 测试 18 passed；全量 dotnet test src\\SpaceMonger.sln --no-restore 49 passed。
- CUA 后台验证：隐藏启动本项目 SpaceMonger.App.exe，UIA 找到 Copilot 输入框，后台写入 @unity-project-cleanup explain risk model from skill only 并重取快照确认，随后关闭测试进程。

## 2026-06-29 21:18:37 +08:00
- 继续去除宿主自然语言硬编码：删除 ChatViewModel 中 TryHandleChatWindowIntent、ClassifyClearConversationIntent、本地中英文短语清空对话识别，以及旧的 ExecuteAutomaticActionAsync 自动执行路径。
- 保留明确 UI 命令 /clear；普通自然语言如 clear chat 现在必须通过模型返回 ClearConversation proposal 才生成确认卡。
- 将确认卡执行的非 Unity action 进度从空列表改成通用确认执行步骤，避免确认后无工作流反馈。
- 验证：ChatViewModelProposalTests 11 passed；dotnet-debug-mcp build_solution 0 errors；全量 dotnet test src\\SpaceMonger.sln --no-restore 49 passed。
- CUA 后台验证：隐藏启动 WPF，写入 clear chat should be handled by model proposal 到 Copilot 输入框并重取 UIA 快照确认，随后关闭测试进程。

## 2026-06-29 21:29:48 +08:00
- 将 skill catalog 从 AiSkillRouter 硬编码三项改为 FileSkillPromptProvider 从 skills/**/SKILL.md 文件发现；display name 来自一级标题，description 来自 ## Purpose 段落。
- 扩展 ISkillPromptProvider.GetSkillCatalog()，router 只负责 @skill 选择和 prompt 注入，不再知道内置技能列表。
- 删除未引用的旧 SkillCatalog 静态双入口，减少 skill 加载路径分叉。
- 新增回归测试：临时目录新增 custom-disk-skill/SKILL.md 后，无需修改 router 源码即可被 catalog 发现并通过 @custom-disk-skill 注入 prompt。
- 验证：AiSkillRouterTests 8 passed；dotnet-debug-mcp build_solution 0 errors；全量 dotnet test src\\SpaceMonger.sln --no-restore 50 passed。
- CUA 后台验证：隐藏启动 WPF，UIA 找到 Copilot 输入框，后台写入 @unity-project-cleanup verify file-discovered skill catalog 并重取快照确认，随后关闭测试进程。

## 2026-06-29 21:40:18 +08:00
- 修复 AgentRuntime 无扫描上下文时 app-level tool 被拒绝的问题：工具先解析，再根据 RequiresScanContext 决定是否需要 scan context。
- propose_copilot_action 开放 DiscoverUnityLibraries/ClearConversation 等符号动作，避免 skill 只能绕过 tool 或依赖旧 UI proposal 解析路径。
- 新增 AgentRuntime 回归测试：无 scan context 下可提出 app-level action，文件树工具仍被拒绝；Unity discovery 和 clear conversation proposal 均能由 tool 返回。
- 验证：AgentRuntimeTests 5 passed；dotnet-debug-mcp build_solution 0 errors；dotnet test src\\SpaceMonger.sln --no-restore 53 passed；CUA 隐藏启动 WPF，后台写入 Copilot 输入框并复核成功。

## 2026-06-29 21:49:45 +08:00
- 删除 router 旧路由兼容字段 CanRunWithoutScanContext/PreferModelAnswer 和 app-guide 特判，router 只保留 @skill 选择与 skill prompt 注入。
- 收紧 AgentRuntime 工具执行边界：文件树工具要求完整 scan context，不再接受 Session/CurrentViewRoot 为空的半上下文。
- FileTreeAgentTools 改为 RequireScanSession 后再调用查询服务，目标 Core 测试中相关 nullable 警告消失。
- 新增回归测试 RunAsync_RejectsScanTreeToolWithIncompleteScanContext。
- 验证：AiSkillRouterTests + AgentRuntimeTests 14 passed；dotnet-debug-mcp build_solution 0 errors；dotnet test src\\SpaceMonger.sln --no-restore 54 passed；CodeGraph synced；CUA 隐藏启动 WPF、后台写入输入框并复核成功。

## 2026-06-29 21:56:45 +08:00
- 修正 AgentRuntime system/user prompt：不再宣称所有 tools 都是 read-only scan-tree 查询；明确 tool call 可返回观察或确认卡 proposal，并按 tool risk/schema 行事。
- 修正无 scan context 的 Host context note：允许 app-level proposal tools 生成扫描/发现确认卡，不再限制为只能解释。
- 新增 AgentRuntime prompt 回归测试 RunAsync_AppOnlyPromptAllowsProposalToolsWithoutPretendingAllToolsAreReadOnly。
- 验证：AgentRuntimeTests 7 passed；dotnet test src\\SpaceMonger.sln --no-restore 55 passed；dotnet-debug-mcp build_solution 0 errors；CodeGraph synced；CUA 隐藏启动 WPF 并后台写入/复核输入框成功。

## 2026-06-29 22:02:53 +08:00
- 修正 ChatService 两条 thinking streaming 路径，将硬编码 file tree/ignored 文案替换为通用 agent tool observation 汇总。
- 新增 ChatServiceStreamingTests 两个回归测试，覆盖 scan context 和 skill/app-only 两条 streaming 路径，防止回退到 file tree/no scan context 文案。
- 验证：ChatServiceStreamingTests 3 passed；AgentRuntimeTests+ChatServiceStreamingTests+AiSkillRouterTests 18 passed；dotnet test src\\SpaceMonger.sln --no-restore 57 passed；dotnet-debug-mcp build_solution 0 errors；CodeGraph synced/status OK。

## 2026-06-29 22:03:42 +08:00
- CUA 后台冒烟：Start-Process -WindowStyle Hidden 启动 SpaceMonger.App.exe，进程 Path 校验匹配；UIA 找到 InputTextBox，后台 set_value 写入 @unity-project-cleanup verify generic agent tool observations，重取快照确认 value 生效；测试进程已关闭。

## 2026-06-29 22:05:14 +08:00
- 补充修正 Strings.resx 中 read-only file tree tools 旧文案为 agent tools。
- 复验：dotnet test src\\SpaceMonger.sln --no-restore 57 passed；dotnet-debug-mcp build_solution 0 errors；CodeGraph status OK。

## 2026-06-29 22:16:54 +08:00
- 取消 AiSkillRouter 默认注入全部 skills，只保留显式 @skill 选择。
- FileSkillPromptProvider 改为按需读取 skill 文件并支持 CreateOrUpdateSkill/DeleteSkill。
- 新增 manage_disk_skills agent tool 并在 App DI 注册，提供 skill CRUD 与磁盘管理/host tool 守门。
- 新增 ManageDiskSkillsToolTests，更新 AiSkillRouterTests。
- 验证：相关 Core 测试 18 passed；dotnet test src\\SpaceMonger.sln --no-restore 60 passed；dotnet-debug-mcp build_solution 0 errors；发布 outputs\\package-2026-06-29-2214；CUA 后台启动发布包并写入 InputTextBox 复核成功。

- [2026-06-30 09:31:20 +08:00] 修复：AiSkillRouter 增加声明式 skill 自动匹配；Unity 清理自然语言请求会加载 skills/unity-project-cleanup/SKILL.md。
- [2026-06-30 09:31:20 +08:00] 修复：DiscoverUnityLibraries 卡片文案改为通用 Discover cleanup candidates。
- [2026-06-30 09:31:20 +08:00] 修复：Unity skill 补充日期/Hub 缺失风险判定规则，明确由 AI 按 skill 证据判定，app 不硬判 Safe/Caution。
- [2026-06-30 09:31:20 +08:00] 验证：dotnet test tests/SpaceMonger.Core.Tests/SpaceMonger.Core.Tests.csproj --filter 'AiSkillRouterTests|AgentRuntimeTests|ManageDiskSkillsToolTests' 通过。

- [2026-06-30 09:34:15 +08:00] 验证：dotnet test src/SpaceMonger.sln 通过（Core 47、App 15）；dotnet publish Release 输出 outputs/package-2026-06-30-0933。
- [2026-06-30 09:34:15 +08:00] CodeGraph 已 sync。

- [2026-06-30 10:05:58 +08:00] 修复：确认/取消交互卡点击后立即移除 overlay，只保留 step 指示器显示流程状态；取消不再触发 follow-up。
- [2026-06-30 10:05:58 +08:00] 修复：Copilot 回答语言以 app 设置优先；只有设置为 auto 时才回退当前 UI/系统语言。
- [2026-06-30 10:05:58 +08:00] 测试：新增虚拟 C/D/E 盘与慢扫描执行器，覆盖俚语/中文请求、英语 app 语言优先、慢扫描等待。
- [2026-06-30 10:05:58 +08:00] 验证：dotnet-debug-mcp build_solution 通过；dotnet test src/SpaceMonger.sln 通过；发布 D:\AppData\Visual Studio\Projects\spacemonger-next\outputs\package-2026-06-30-1005。

- [2026-06-30 10:24:16 +08:00] 修复：ApplyProposalIfAny 兼容 wrapped proposal 与 snake_case action kind，解决 hasProposal=True 但确认卡/step 不出现。
- [2026-06-30 10:24:16 +08:00] 测试：新增 wrapped proposal -> PendingInteractionCard -> Confirm -> workflow step 可见回归。
- [2026-06-30 10:24:16 +08:00] 验证：dotnet-debug-mcp build_solution 通过；dotnet test src/SpaceMonger.sln 通过；发布 D:\AppData\Visual Studio\Projects\spacemonger-next\outputs\package-2026-06-30-1024。

## 2026-06-30 18:50:02 +08:00
- 修复 AI 外部分析等待状态下点击分析/清理按钮仍使用 Windows MessageBox 的问题。
- RecommendationsPanel 改为通过 ShowWaitingForAiMessageAsync 委托请求宿主显示提示，MainWindow 注入现有 AppModalHost 通用模态窗口。
- 验证：dotnet test src\\SpaceMonger.sln --no-restore 通过（73 passed，保留既有 NU1701/CS8604 warning）；CodeGraph sync 完成；发布 outputs\\SpaceMonger-win-x64-folder-20260630-184943。

## 2026-06-30 19:05:52 +08:00
- 修复 Chat slash command 描述：改为 Strings.resx/Strings.zh-CN.resx 本地化资源，移除 ChatViewModel 中的乱码中文硬编码。
- 新增 /clear console 指令：从聊天输入触发 AppLog.UiSink.Clear，只清空应用内 Console，不清空聊天记录/扫描数据。
- 测试：ChatViewModelProposalTests 新增多语言描述和 console 清空回归；dotnet test src\\SpaceMonger.sln --no-restore 通过（75 passed，保留既有 NU1701/CS8604 warning）。
- 发布：outputs\\SpaceMonger-win-x64-folder-20260630-190534；CodeGraph sync 完成。
