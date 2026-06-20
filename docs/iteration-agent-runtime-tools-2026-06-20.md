# Agent Runtime 与文件树工具调用迭代纪要

日期：2026-06-20  
主题：Phase 1 Agent Runtime / Tool Calling 基础能力

## 本轮完成

- 新增 provider-neutral 的 Agent Tool Call IR：`AgentToolCall`、`AgentToolResult`、`AgentRequest`、`AgentResponse`、`AgentContext`、`ToolRiskLevel`。
- 新增 `AgentRuntime`，通过 JSON fallback 协议驱动最多 4 轮、最多 8 次只读工具调用，并限制单轮 observation 注入长度。
- 新增 `IFileTreeQueryService` 与 `FileTreeQueryService`，直接查询 `ScanSession.RootEntry` / `FileEntry.Children`，不重新扫描磁盘。
- 将 `ChatService` 接入 Agent Runtime，保留现有聊天 UI 与 `IChatService` 方法签名。
- 新增 Core 单元测试，覆盖路径归一化、名称搜索、子节点列举、子树摘要、大文件查询和部分工具结构化返回。

## 当前内置工具

- `find_by_name`：按名称大小写不敏感查找已扫描文件或目录。
- `find_by_path`：按路径查找已扫描节点，支持 Windows 路径大小写不敏感和斜杠归一化。
- `list_children`：列出目录直属子项，按大小降序。
- `summarize_subtree`：汇总子树大小、文件/目录数量、最近修改时间和最大直属子项。
- `find_large_files`：在全树或指定目录下查找最大文件。

所有工具当前均为 `ReadOnly` 风险等级，只读取内存中的扫描树，失败时返回 `{ ok: false, error: ... }` 结构，不向 UI 直接抛出业务错误。

## JSON fallback 协议

当前没有改造 `ILlmClient` 为供应商原生 tool calling。Runtime 会在 system prompt 中声明工具 schema，并要求模型在需要查询时输出：

```json
{
  "tool_calls": [
    {
      "id": "call_1",
      "name": "find_large_files",
      "arguments": {
        "under_path": "C:\\Scan\\Models",
        "max_results": 10
      }
    }
  ]
}
```

应用解析后执行工具，把 observation 追加回对话，再让模型继续生成最终回答。这样 Anthropic / OpenAI / DeepSeek 等 provider 私有格式仍可留在后续 adapter 层扩展。

## 限制

- 当前聊天流式 UI 保留，但 Agent Runtime 内部使用非流式 `SendChatAsync` 完成工具循环，最终答案一次性写入现有气泡。
- 工具调用过程只以简短 thinking 文本提示“查询了只读文件树工具”，尚未实现折叠式工具调用 UI。
- 没有实现 Skill、MCP、Unity/Steam 专业分析、WebView2 动态结果渲染。
- 没有写操作工具；删除、移动、隔离等能力仍不允许由 Agent 执行。

## 后续扩展方向

- 在 `ILlmClient` 上增加 provider-neutral 的 tool calling 方法，并在 Anthropic/OpenAI/DeepSeek adapter 内做私有格式翻译。
- 把 `IAgentTool` 与 schema/risk metadata 作为 Skill manifest 和 MCP adapter 的统一接入面。
- 增加工具调用过程 UI：展示工具名称、参数摘要、结果摘要、错误和截断状态。
- 在后续 Phase 2/5 中将 Skill/MCP 工具统一转换为 `IAgentTool`，继续由 `AgentRuntime` 负责安全限制、循环和 observation 管理。
