# 2026-06-26 Copilot Skills 打包与自然说明链路

## 背景
用户希望把本轮对话中整理出的磁盘管理相关 skill 和 APP 说明沉淀到项目根目录 `skills`，并让 Agent Chat 能按需自动唤起。用户特别指出，“推荐清理是什么”这类自然语言问题不应被粗糙理解为“现在要清理/需要先扫描”，而应作为模块解释问题自然回答。

## 本轮实现
- 新建根目录 `skills`：
  - `skills/disk-management/SKILL.md`：整理扫描、文件树查询、目录清理分析、推荐清理、Treemap 导航等磁盘管理能力。
  - `skills/app-guide/SKILL.md`：整理 APP 身份、扫描、Treemap、TreeView、推荐清理、AI Chat、设置/API Key、白名单、控制台等模块说明。
- 在 `SpaceMonger.App.csproj` 中登记 `skills/**` 为 Content：
  - Debug/Build 输出复制到 `bin/.../skills`。
  - Publish 输出复制到发布目录。
- 在 `SpaceMonger.sln` 中新增 `skills` solution folder，把两个 `SKILL.md` 作为 Solution Items，方便 IDE 中维护。
- 扩展 `ModuleHelp`：
  - 增加“是什么/介绍/解释/说明/入口”等自然说明问法。
  - `推荐清理是什么？` 会识别为模块解释，不再生成推荐分析 action。
  - 说明类问题 `CanRunWithoutScanContext=true`，可在未扫描时进入模型回答。
  - 有 API Key 时优先让模型结合 `module_help` skill 回答；无 API Key 时使用本地兜底回答。
- 扩展 Agent/Chat：
  - `AgentRuntime` 支持无扫描上下文模式，但无上下文时不会执行文件树工具。
  - `ChatService` 增加 skill-only 流式入口。
  - `ChatViewModel` 在说明类 skill 上允许无扫描上下文对话，不再强制提示“请先完成扫描”。

## 设计边界
- skill 文件作为长期沉淀和打包资产；运行时仍通过 `AiSkillRouter` 按需注入对应 skill prompt，避免全局 prompt 冗余。
- 无扫描上下文时只允许说明/身份类回答，不允许文件树工具声称有扫描数据。
- 仍不开放主题、捐赠、关于页跳转等非磁盘管理控制动作。

## 验证
- `dotnet test tests/SpaceMonger.Core.Tests/SpaceMonger.Core.Tests.csproj --no-restore`：通过。
- `dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj --no-restore`：通过，存在项目既有兼容性/nullable/resource warning。
- 已确认 Debug 输出目录包含 `skills/app-guide/SKILL.md` 与 `skills/disk-management/SKILL.md`。

## 手工测试建议
- 未扫描但已配置 API Key：输入 `推荐清理是什么？`，预期模型自然解释推荐清理模块，不弹确认卡片，不提示必须先扫描。
- 未配置 API Key：输入 `Treemap 有什么用？`，预期本地兜底说明。
- 已扫描后：输入 `这个文件夹有啥可清理的`，预期仍生成一级确认卡片，而不是普通解释。
