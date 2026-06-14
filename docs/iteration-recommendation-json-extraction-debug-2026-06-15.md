# 迭代纪要：推荐清理 JSON 提取失败断点与修复（2026-06-15）

## 背景

用户在完整流程“运行 → 扫描 → 推荐清理”中遇到偶发 0 推荐。最后一次异常日志位于 `%LOCALAPPDATA%\SpaceMonger.Next\logs\console-20260615-013548.log`，关键诊断为：

- `response_chars=6428`：LLM 实际返回了内容。
- `extracted_json_chars=0` / `parsed_recs=0`：应用未提取出 JSON。
- `parse_error=No JSON object or JSON code block was found in the LLM response.`
- `response_preview` 以 ````json { "recommendations": [...]` 开头，说明响应中已有推荐，只是 markdown code fence 可能未闭合。

## 根因

`RecommendationEngine.ExtractJsonFromResponse()` 原逻辑只支持：

1. 完整闭合的 ````json ... ``` code block。
2. 完整闭合的普通 ``` ... ``` code block。
3. 响应直接以 `{` 开头的 raw JSON。

当 provider 返回未闭合的 ````json` fenced block，或在 JSON 前后夹带说明文本时，解析器会返回 `null`，导致 UI 误显示“分析完成：找到 0 条推荐”。

## 改动

- 新增可控断点钩子：
  - `SPACEMONGER_DEBUG_BREAKPOINTS`：逗号、分号或空格分隔断点名；`all` 命中全部。
  - `SPACEMONGER_DEBUG_LAUNCH=1`：未附加调试器时尝试弹出调试器选择。
- 在扫描完成、分析上下文、LLM 请求/响应、解析完成、推荐应用等关键点挂断点名。
- `ExtractJsonFromResponse()` 增强：
  - 支持未闭合 ````json` / ``` fenced block。
  - 支持从包含额外说明的响应中按 `{ ... }` 平衡括号提取 JSON。
  - 正确处理字符串内的转义和大括号。
- 新增 Core 回归测试，复现日志中的“未闭合 ````json` 但 JSON 内容有效”的情况。

## DeepSeek Anthropic 端点修正

用户实际使用的 Base URL 是 `https://api.deepseek.com/anthropic`。根据 DeepSeek Anthropic API 文档，`claude-sonnet*` 模型名会被映射到 `deepseek-v4-flash`。这对“推荐清理”这种长输入、结构化 JSON 输出任务不稳，容易消耗输出预算或出现不完整 JSON。

后续修正：

- 当 Base URL host 包含 `deepseek.com` 时，推荐分析使用 `deepseek-v4-pro`。
- DeepSeek 端点下请求体增加 `thinking: { type: "disabled" }`，避免 thinking 消耗输出 token 预算。
- 聊天继续使用 `deepseek-v4-flash`，保持交互速度。
- 控制台新增 `response_envelope_path` 和 `stop_reason`，用于确认是否为 `max_tokens` / `length` 截断。
- 推荐上限从临时止血的 5 条放宽到 20 条，并保留短 explanation 约束。

## 自测结果

- `dotnet test .\tests\SpaceMonger.Core.Tests\SpaceMonger.Core.Tests.csproj -c Debug --no-restore`：通过。
- `dotnet test .\src\SpaceMonger.sln -c Debug --no-restore`：通过。
- `dotnet build .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Debug`：通过，仅保留既有 `NU1701` 包兼容警告。
- 使用 Computer Use 做真实桌面点击自测时，插件初始化失败：`@oai/sky` package exports 缺失。
- 使用 `dotnet run` 启动 WPF 应用时被 manifest 阻止：`requestedExecutionLevel=requireAdministrator`，当前非提升进程无法启动，错误为“请求的操作需要提升”。

## 手动断点建议

若要在 Visual Studio / Rider 中完整跑 UI 流程，可用管理员权限启动 IDE，并设置：

```powershell
$env:SPACEMONGER_DEBUG_BREAKPOINTS='analysis-response-received,analysis-response-parsed,recommendations-applied'
$env:SPACEMONGER_DEBUG_LAUNCH='0'
```

推荐优先断在：

- `analysis-response-received`：查看原始 LLM 文本是否已有推荐。
- `analysis-response-parsed`：查看 `ExtractedJsonLength`、`ParseError`、`ParsedRecommendationCount`。
- `recommendations-applied`：确认 UI ViewModel 是否收到推荐集合。
