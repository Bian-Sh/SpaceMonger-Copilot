# 2026-06-26 Agent-First Copilot 重构中段纪要

## 本轮目标
- 废弃“先本地关键字判断，再决定是否进入模型”的主流程。
- 改为 `agent-first`：有模型时，用户问题直接交给 AgentRuntime 理解。
- 当用户问到未扫描路径/目录问题时，不再停在“请先完成扫描”，而是由 agent 主动提议是否扫描。
- 一级确认卡片内容支持复制。

## 本轮已完成
- Chat 主流程切到 `agent-first`：
  - `ChatViewModel` 不再依赖 `IAiSkillRouter` 决定主链路。
  - 无 API Key 时直接提示用户配置模型。
- 新增 agent 工具：
  - `get_copilot_context`
  - `propose_copilot_action`
- `AgentRuntime`：
  - 增加 agent-first 指导语。
  - 明确要求在无扫描/未命中扫描树时优先提议扫描动作卡片。
  - 从 tool observation 中提取 `proposal`。
- `ChatService` / `ChatViewModel`：
  - 允许把 `proposal` 从 agent 返回到 UI。
  - 将 `proposal` 转换为 `AiInteractionCard`。
- `ChatPanel.xaml`：
  - 卡片中的 `Description / Impact / StatusText` 改为只读 `TextBox`，支持复制。

## 当前边界
- 当前 `proposal -> InteractionCard` 链路已接通，但还需要继续做更系统的自动化测试与真实推荐状态注入。
- 现阶段 `ChatService` 传给 `AgentContext` 的 `HasExistingRecommendations` 仍为保守默认值，下一轮可再接入真实状态。

## 体验变化
- 无模型：直接提示配置模型。
- 有模型但未扫描：agent 应该更倾向于给出“是否扫描该路径”的一级确认卡片，而不是停住。
- 卡片文案现在可复制。
