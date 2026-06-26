# 2026-06-26 Copilot 多语言回答对齐

## 背景
用户要求确保 AI 回答内容自动适配 APP 的多语言设置，不论答案来自：
- Agent / LLM
- skill 记录 / skill prompt
- 本地 fallback 文案

## 本轮实现
- `ChatViewModel` 发送消息时读取设置中的 `Language`，并解析为本轮 `responseLanguage`：
  - `auto` 或空值：使用当前 UI 语言 `L.CurrentLanguageName`
  - 显式值如 `zh-CN` / `en`：直接使用该语言
- `AiSkillRouter.Route(...)` 新增 `responseLanguage` 参数：
  - 本地 fallback 说明类回答会按语言选择中文或英文
  - identity fallback 也会按语言选择中文或英文
- `IChatService` / `ChatService` / `IAgentRuntime` / `AgentRuntime` 新增 `responseLanguage` 传递链路。
- `AgentRuntime.BuildSystemPrompt(...)` 增加明确约束：
  - 回答语言必须匹配 APP UI 语言/配置语言
  - 即使 skill 源文本或拉取记录本身是英文，也不能把英文原样当最终用户回答语言
- skill-only 无扫描上下文模式也走同样语言规则。

## 结果
- 当 APP 语言设为中文时：
  - 模型回答、skill 驱动回答、本地兜底回答都应优先中文。
- 当 APP 语言设为英文时：
  - 模型回答、skill 驱动回答、本地兜底回答都应优先英文。
- `skills/*.md` 仍可以用英文编写，不影响最终面向用户的回答语言。

## 验证建议
- APP 语言设为 `en`：
  - 问 `What is Treemap for?`
  - 问 `Who are you?`
  - 预期为英文回答。
- APP 语言设为 `zh-CN`：
  - 问 `Treemap 有什么用？`
  - 问 `你是谁？`
  - 预期为中文回答。
- APP 语言设为 `en` 且已配置 API Key：
  - 问 `What are cleanup recommendations?`
  - 预期模型用英文自然解释，不受 skill 文件英文/中文书写差异影响。
