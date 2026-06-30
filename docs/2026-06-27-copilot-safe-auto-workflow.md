# 2026-06-27 Copilot 安全自动工作流

## 背景

本轮目标是让 SpaceMonger Copilot 更接近 Codex 式交互：用户提出明确可执行的磁盘/Unity 清理意图时，应用应直接展示确认或进度 UI，而不是在聊天气泡里反复询问“要不要弹窗”。

## 已实现

- 对安全的“扫描 + 清理分析”意图，Copilot 可自动执行扫描，并在没有旧推荐结果时继续触发推荐分析。
- 对会覆盖旧推荐结果的分析，只保留一次确认卡片，不再二次询问。
- 支持中文 `D盘`、`D 盘` 这类驱动器写法，并映射到 `D:\`。
- 输入框支持 `/new` 与 `/clear`，输入 `/` 时在输入框上方展示命令和说明。
- 交互卡片左侧补齐三态图标：`idle`、`running`、`finish`。
- 输入框上方显示确认卡片；消息气泡内不再承载确认弹窗。
- 消息区顶部显示异步计划步骤浮层：`第 X/Y 步` 与每一步状态。
- 普通扫描和 Copilot 扫描前都会尝试动态 UAC：非管理员实例会用 `runas` 启动管理员实例，并通过 `--scan <path>` 自动继续扫描。
- 新增 Unity 项目清理 skill，用于识别 Unity 项目、读取 Unity Hub 项目清单、评估 `Library` 清理风险。

## Unity 清理规则摘要

- 强 Unity 项目标记：`ProjectSettings/ProjectVersion.txt` + `Assets/`。
- 支撑标记：`Packages/manifest.json`、`Packages/packages-lock.json`、`UserSettings/`、Unity 生成的 `.csproj`、`Library/`。
- `Assets/`、`ProjectSettings/`、`Packages/` 不应作为清理候选。
- `Library/` 通常可重建，但 Hub 中存在的项目最低风险为 `medium`，近期活跃项目为 `high`。
- Windows 上优先读取 `%APPDATA%\UnityHub\projects-v1.json` 与 `%APPDATA%\UnityHub\projectDir.json`，注册表仅作为 Unity/Hub/Editor 安装上下文的辅助证据。

## 验证

- `dotnet test tests/SpaceMonger.App.Tests/SpaceMonger.App.Tests.csproj --no-restore --filter "ChatViewModelProposalTests"`：通过 6 个测试。
- `dotnet test tests/SpaceMonger.Core.Tests/SpaceMonger.Core.Tests.csproj --no-restore --filter "AiSkillRouterTests|AiInteractionCardTests"`：通过 28 个测试。
- `dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj -c Debug --no-restore`：通过；仍有既有 `NU1701`、nullable 和重复资源名警告。

## 后续

- 已补齐 Codex 式多步骤浮层：消息区顶部居中显示 `第 X/Y 步`，并列出每一步的 `idle/running/finish` 状态。
- 可进一步把 Unity Hub/注册表读取做成实际扫描工具，而不仅是 skill 规则。