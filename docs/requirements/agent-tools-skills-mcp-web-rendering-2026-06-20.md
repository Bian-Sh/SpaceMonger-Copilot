# AI Agent 工具调用、Skill 生态与动态结果渲染需求架构纪要

日期：2026-06-20  
主题：将聊天式磁盘分析升级为可主动查询文件系统、加载领域 Skill、支持 MCP 扩展与动态 UI 渲染的 Agent 架构

## 背景

当前应用已经具备磁盘扫描、Treemap 可视化、AI 推荐和聊天能力，但聊天体验仍偏向“把当前视图摘要塞给模型”。当用户扫描某个大型目录后询问“深入分析某个子目录”时，AI 只能看到当前目录的一层子项；如果用户没有手动进入目标目录，AI 无法主动读取其内部结构，也无法继续下钻分析。

本次头脑风暴形成的核心判断是：产品不应该只是“带 AI 的磁盘分析器”，而应该升级为“文件系统 Agent”。AI 应能围绕已扫描数据主动发起查询、观察结果、继续推理，并在安全门控下给出清理方案。

同时，领域能力不宜全部做死在主程序中。类似 ComfyUI、Unity、SteamLibrary、开发缓存、游戏资源迁移等场景，应该以 Skill 形式随 APP 安装目录附带，未来也能支持用户自行扩展或通过 MCP 接入。

## 当前代码现状

### 已具备的数据基础

- `ScanSession.RootEntry` 已持有完整扫描树。
- `FileEntry.Children` 已表达递归目录结构。
- 扫描器已能得到路径、大小、目录/文件类型、修改时间等基础元数据。
- 推荐引擎已经能基于全量扫描树构建压缩摘要。

### 当前缺口

聊天链路目前是一次性上下文注入：

```text
ScanSession.RootEntry
    -> Treemap 当前目录 currentViewRoot
    -> ChatService.BuildContextBlock
    -> current_view_items = currentViewRoot.Children
    -> LLM 纯文本回复
```

主要问题：

1. AI 只能看到当前视图一级 children，不能自行查询任意子路径。
2. 用户提到目录名时，本地没有先帮 AI 定位对应 `FileEntry`。
3. `ILlmClient` 只支持纯文本 chat/analysis，没有 tool calling 抽象。
4. 底层请求体没有 `tools`、`tool_choice` 或等价字段。
5. 推荐分析生成的是一次性压缩元数据，不是 AI 可迭代查询的索引服务。
6. 没有领域 Skill 系统，无法表达 Unity 项目清理、SteamLibrary 分析、ComfyUI 模型分析等专项能力。
7. AI 输出仍是静态 Markdown，无法把大批对象渲染成可交互、一致样式的结果界面。

## 产品目标

将 SpaceMonger.Next 的 AI 能力从“问答助手”升级为“可调查、可验证、可解释、可安全执行、可动态展示结果的磁盘 Agent”。

目标体验：

用户输入：

> 帮我找一下本机有哪些 Unity 项目，哪些 Library 可以清理。

AI 应自动执行类似流程：

```text
find_candidate_unity_projects(scanRoot)
verify_unity_project(path)
read_unity_hub_recent_projects()
compare_with_active_projects(...)
summarize_library_size(projectPath)
generate_cleanup_plan(...)
render_result_card_grid(...)
```

最终输出：

- 找到哪些 Unity 项目。
- 每个项目的 `Library`、`Temp`、`Obj`、`Logs` 等目录占用。
- 哪些项目出现在 Unity Hub 最近项目或已注册项目中。
- 哪些项目疑似长时间未打开。
- 哪些 `Library` 可以安全删除并由 Unity 重新生成。
- 以一致样式的卡片/表格展示清理候选。

## 核心需求

### R1：内置文件树查询工具

提供一组只读工具，让 AI 可主动查询扫描树。

建议工具：

```text
find_by_name(nameOrPattern, rootPath?, maxResults)
find_by_path(path)
list_children(path, sortBy, limit)
summarize_subtree(path, depth?)
find_large_files(path, minBytes, limit)
group_by_extension(path, limit)
find_duplicate_candidates(path, minBytes, matchBy)
get_entry_details(path)
```

工具返回结构化 JSON，不直接返回 UI 文本。

### R2：Agent Runtime

增加应用内 Agent Runtime，负责模型与工具之间的循环。

职责：

1. 接收用户消息和当前扫描会话。
2. 构造系统提示和可用工具描述。
3. 调用 LLM。
4. 解析模型提出的工具调用。
5. 执行本地工具。
6. 将 observation 继续发给模型。
7. 限制最大调用轮次、最大数据量、最大耗时。
8. 输出最终回答和可选动态 UI 描述。

建议默认限制：

- 单次消息最多 8 次工具调用。
- 单个工具返回最多 100 项或 64KB JSON。
- 单轮 Agent 最长 60 秒。
- 写操作默认禁用。

### R3：Provider Adapter，不绑定单一模型格式

不要把项目内部直接绑定到 Anthropic、OpenAI、DeepSeek 或任何模型供应商的工具调用格式。应定义内部 Tool Call IR：

```csharp
public sealed record AgentToolCall(
    string Id,
    string Name,
    JsonObject Arguments);

public sealed record AgentToolResult(
    string ToolCallId,
    bool IsError,
    JsonObject Payload,
    string? ErrorMessage);
```

#### 统一 Tool Call IR 是架构边界

这是 Agent 体系中最重要的抽象边界。产品内部不应该出现大量类似 `AnthropicToolUseBlock`、`OpenAIToolCall`、`DeepSeekToolCall` 的业务依赖；这些 provider 私有结构只能存在于 adapter 层。业务层、Skill 层、MCP 层和 UI 层都只认识 SpaceMonger 自己的 Tool Call IR。

推荐边界：

```text
LLM Provider 私有格式
    -> Provider Adapter
    -> SpaceMonger Tool Call IR
    -> Agent Runtime
    -> IAgentTool
    -> Observation IR
    -> Provider Adapter
    -> LLM Provider 私有格式
```

这样可以避免三类长期风险：

1. 切换模型供应商时重写业务逻辑。
2. 不同供应商工具调用字段差异污染 Agent Runtime。
3. MCP、内置工具、Skill、UI 调查过程无法复用同一套执行链路。

Provider Adapter 只负责格式翻译，不负责业务判断：

```text
AnthropicAdapter：Anthropic tool_use/tool_result <-> AgentToolCall/AgentToolResult
OpenAIAdapter：OpenAI tool_calls/tool outputs <-> AgentToolCall/AgentToolResult
DeepSeekAdapter：DeepSeek 兼容工具格式或降级协议 <-> AgentToolCall/AgentToolResult
JsonFallbackAdapter：纯文本 JSON 协议 <-> AgentToolCall/AgentToolResult
McpAdapter：MCP tool schema/result <-> AgentToolCall/AgentToolResult
```

Agent Runtime 只处理统一语义：

```text
模型请求调用哪个工具
参数是否符合 schema
工具风险等级是否允许
执行结果是否需要压缩
是否继续下一轮工具调用
何时生成最终回答
是否需要生成动态 UI 描述
```

MCP 应作为“外接工具来源”，而不是另一套 Agent 运行时。MCP server 暴露出来的工具进入系统后，也应该先注册为 `IAgentTool`，再由统一 Runtime 调度。

### R4：Skill 生态

Skill 是领域知识、工具集合、提示策略、结果渲染模板和安全策略的组合。它不等于主程序硬编码功能。

建议 Skill 结构：

```text
Skill
├── id
├── displayName
├── description
├── activationRules
├── systemPromptAppendix
├── allowedTools
├── customTools
├── uiTemplates
├── safetyPolicy
└── resultSchema
```

Skill 来源：

1. APP 安装目录内置附带。
2. 用户本地安装。
3. 团队共享目录。
4. 未来从网络仓库/市场下载。
5. MCP server 动态提供。

Skill 运行原则：

- 主程序提供通用 Agent Runtime 和基础文件工具。
- Skill 提供领域识别规则、专项工具、提示词、UI 模板和风险策略。
- Skill 可以被禁用、启用、升级、卸载。
- 高风险 Skill 默认只读，写操作必须用户确认。

### R5：Unity 项目清理 Skill

这是当前最适合作为内置附带 Skill 的专业场景之一，尤其适合经常从网络拉取开源 Unity 项目测试的用户。

#### 识别 Unity 项目

判断一个目录是否为 Unity 项目，可以综合以下信号：

```text
Assets/
ProjectSettings/
Packages/
ProjectSettings/ProjectVersion.txt
Packages/manifest.json
Assets/*.asmdef
*.unity
*.prefab
*.mat
*.asset
```

强判断：

- 同时存在 `Assets` 和 `ProjectSettings`。
- 存在 `ProjectSettings/ProjectVersion.txt`。

弱判断：

- 存在 `Packages/manifest.json`。
- 子目录中存在大量 `.unity`、`.prefab`、`.asset`、`.asmdef` 文件。

#### 可清理对象

Unity 项目中常见可再生成或低风险清理目录：

```text
Library/
Temp/
Obj/
Logs/
Build/
Builds/
UserSettings/Layouts/CurrentMaximizeLayout.dwlt
```

其中 `Library` 通常是最大空间占用来源，删除后 Unity 会重新导入资产并重建缓存；代价是下次打开项目会很慢。

#### Unity Hub 活跃项目判断

Skill 应提供只读工具读取 Unity Hub 记录，判断哪些项目仍在开发或近期打开过。

Windows 上可检查来源包括：

```text
%APPDATA%/UnityHub/
%APPDATA%/UnityHub/editors.json
%APPDATA%/UnityHub/projects-v1.json
%APPDATA%/UnityHub/secondaryInstallPath.json
注册表 HKCU/HKLM 中 Unity Technologies 或 UnityHub 相关键
```

注意：Unity Hub 版本不同，项目记录文件可能变化。Skill 需要采用多来源探测：

1. 先查 Unity Hub 配置文件。
2. 再查注册表。
3. 再查最近访问时间和项目文件修改时间。
4. 最后让用户人工确认。

#### Unity Skill 工具建议

```text
find_unity_projects(rootPath)
verify_unity_project(path)
read_unity_hub_projects()
read_unity_registry_hints()
summarize_unity_cache(projectPath)
classify_unity_project_activity(projectPath)
generate_unity_cleanup_plan(projectPath)
```

#### Unity 清理建议等级

| 等级 | 判断依据 | 默认建议 |
|---|---|---|
| HighConfidence | Unity 项目明确、未出现在 Hub 最近项目、长期未修改、Library 巨大 | 建议清理 Library |
| ReviewFirst | Unity 项目明确，但近期打开或 Hub 有记录 | 人工确认 |
| Keep | 当前活跃开发项目、近期修改频繁 | 不建议清理 |
| Unknown | 识别信号不足 | 不建议自动清理 |

### R6：SteamLibrary 分析 Skill

面向游戏专业户或 Steam 用户，Skill 可识别 Steam 库并分析游戏占用、缓存、Workshop、shader cache 等空间。

#### Steam 库识别

可检查：

```text
steamapps/libraryfolders.vdf
steamapps/appmanifest_*.acf
steamapps/common/
steamapps/workshop/
```

Steam 安装和库路径可从以下来源探测：

```text
注册表 SteamPath / InstallPath
Steam/config/libraryfolders.vdf
各磁盘中的 SteamLibrary/steamapps/libraryfolders.vdf
```

#### Steam Skill 工具建议

```text
find_steam_libraries(rootPath)
read_steam_libraryfolders()
read_app_manifests(libraryPath)
summarize_steam_apps(libraryPath)
summarize_workshop_content(libraryPath)
find_orphan_steam_content(libraryPath)
generate_steam_cleanup_plan(libraryPath)
```

#### Steam 清理分析方向

1. 游戏本体占用排序。
2. Workshop 内容占用排序。
3. 已卸载但残留的 app/workshop 内容候选。
4. shader cache/download cache 等缓存候选。
5. 多库迁移建议：大游戏迁移到空间更大的盘。

### R7：ComfyUI 等专业场景作为非内置或可选 Skill

ComfyUI 模型分析、workflow 反查模型引用关系等能力很有价值，但不应写进当前主程序内置需求。它应作为可选 Skill 存在：

```text
skills/comfyui-model-analysis/
```

该 Skill 可以在未来实现：

- 识别 ComfyUI 根目录。
- 分析 `models` 目录占用。
- 可选解析 workflow JSON。
- 交叉比对 workflow 中的模型引用和磁盘模型文件。
- 找出未被 workflow 引用的模型候选。

当前主需求只要求 Agent Runtime 和 Skill 机制具备承载这类能力的架构，不要求主程序直接内置 ComfyUI workflow 反查逻辑。

### R8：Web 渲染与 JS 联动结果面板

AI 对象列表不适合全部用 Markdown 展示。对于 Unity 项目、Steam 游戏、模型文件、重复文件等对象，应该支持由 AI 或 Skill 生成结构化数据，再由应用用统一 HTML 模板动态渲染。

#### 设计目标

1. AI 负责生成结构化结果，不直接生成任意 HTML。
2. APP 提供固定 UI 模板和 CSS，保证视觉一致性。
3. JS 只处理排序、筛选、展开、勾选、事件回传。
4. 用户可以在结果卡片中选择候选项，再触发清理计划。
5. 写操作仍由 WPF 原生确认框或受控命令执行，不让网页直接删除文件。

#### 推荐架构

```text
Agent Final Response
    -> Text Summary
    -> Structured Result JSON
    -> UiRenderSpec
        -> TemplateId
        -> Data
        -> Actions
    -> WebView2 Renderer
        -> HTML Template
        -> CSS Theme
        -> JS Interaction
    -> WPF Host Action Bridge
```

#### UiRenderSpec 示例

```json
{
  "templateId": "cleanup-candidate-grid",
  "title": "Unity Library 清理候选",
  "columns": ["projectName", "librarySize", "lastModified", "activity", "recommendation"],
  "items": [],
  "actions": [
    { "id": "select", "label": "选择" },
    { "id": "showDetails", "label": "详情" },
    { "id": "createCleanupPlan", "label": "生成清理计划" }
  ]
}
```

#### 安全边界

- HTML 模板来自 APP 或已安装 Skill，不接受模型生成的任意脚本。
- 模型只能输出 JSON 数据和模板 ID。
- JS action 只能调用白名单 bridge。
- 文件删除/移动操作必须回到 WPF 安全确认层。

## 推荐架构

```text
ChatViewModel
    -> AgentChatService
        -> AgentRuntime
            -> LlmProviderAdapter
            -> AgentToolRegistry
                -> FileTreeTools
                -> SkillProvidedTools
                -> FutureMcpTools
            -> SkillRegistry
            -> SafetyPolicy
            -> ObservationCompressor
            -> UiRenderSpecBuilder
        -> ResultRenderer
            -> MarkdownRenderer
            -> WebView2Renderer
```

### 新增核心接口建议

```csharp
public interface IAgentRuntime
{
    Task<AgentResponse> RunAsync(AgentRequest request, CancellationToken cancellationToken);
}

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    JsonObject InputSchema { get; }
    ToolRiskLevel RiskLevel { get; }
    Task<AgentToolResult> ExecuteAsync(JsonObject arguments, AgentContext context, CancellationToken cancellationToken);
}

public interface IAgentToolRegistry
{
    IReadOnlyList<IAgentTool> GetTools(AgentContext context);
    IAgentTool? Find(string name);
}

public interface ISkillRegistry
{
    IReadOnlyList<SkillManifest> GetEnabledSkills();
    IReadOnlyList<IAgentTool> GetSkillTools(AgentContext context);
    IReadOnlyList<UiTemplate> GetUiTemplates();
}

public interface IFileTreeQueryService
{
    FileEntry? FindByPath(ScanSession session, string path);
    IReadOnlyList<FileEntry> FindByName(ScanSession session, string pattern, int maxResults);
    IReadOnlyList<FileEntry> ListChildren(FileEntry entry, SortMode sortBy, int limit);
    SubtreeSummary Summarize(FileEntry entry, int maxDepth);
}
```

## MVP 实施路线

### Phase 1：Agent Runtime 与文件树工具

目标：最快解决“AI 不能自己进入子目录”的问题。

内容：

1. 新增 `IFileTreeQueryService`。
2. 新增 `AgentToolRegistry` 和基础只读工具。
3. 定义统一 Tool Call IR。
4. 实现本地 JSON tool call fallback。
5. 限制最大工具调用轮数。
6. 聊天面板展示“AI 正在查询...”过程。

### Phase 2：Skill 机制

内容：

1. 定义 Skill manifest。
2. 从 APP 安装目录加载 Skill。
3. Skill 可声明 activation rules、allowed tools、ui templates。
4. 实现 Skill 启用/禁用。
5. 首批附带 Unity 和 SteamLibrary Skill。

### Phase 3：Unity 项目清理 Skill

内容：

1. 查找 Unity 项目。
2. 读取 Unity Hub 记录和注册表线索。
3. 分析 `Library`、`Temp`、`Obj`、`Logs` 占用。
4. 判断项目活跃度。
5. 生成可清理候选和解释。
6. 用动态 Web 面板展示项目卡片。

### Phase 4：SteamLibrary Skill

内容：

1. 查找 Steam 库路径。
2. 读取 libraryfolders 和 appmanifest。
3. 汇总游戏本体、Workshop、残留内容。
4. 生成迁移/清理建议。
5. 用动态 Web 面板展示游戏列表、筛选和排序。

### Phase 5：MCP Adapter 与外部 Skill

内容：

1. 支持加载外部 MCP server。
2. 将 MCP tool 转换为 `IAgentTool`。
3. 支持第三方 Skill 引用 MCP 工具。
4. 支持非内置 ComfyUI Skill 等专业扩展。

### Phase 6：WebView2 动态结果渲染

内容：

1. 定义 `UiRenderSpec`。
2. 内置统一 CSS 和模板。
3. WebView2 加载本地模板。
4. JS bridge 回传用户选择。
5. WPF 安全层执行确认后的操作。

## UX 设计要点

### 工具调用过程可视化

聊天中应展示折叠式过程：

```text
AI 调查过程
- 查询扫描树中的 Unity 项目候选
- 验证 ProjectSettings/ProjectVersion.txt
- 读取 Unity Hub 最近项目记录
- 统计 Library 目录占用
- 生成清理候选卡片
```

### 结果要证据化

每条清理建议都应包含：

```text
路径
大小
识别依据
最近修改时间
是否出现在 Hub/Steam 等活动记录中
风险等级
建议动作
```

### 默认保守

默认清理动作应为：

```text
生成计划 -> 用户确认 -> 移动到隔离区或删除可再生成缓存 -> 记录日志
```

而不是直接删除。

## 风险与对策

### 风险 1：活跃项目误判

Unity Hub 记录不一定完整，用户可能不用 Hub 打开项目。

对策：

- 综合 Hub 记录、注册表、最近修改时间、项目路径、用户确认。
- 活跃度不确定时标为 `ReviewFirst`。

### 风险 2：Skill 质量不一致

第三方 Skill 可能提示词差、工具 schema 不稳定。

对策：

- Skill manifest 校验。
- 工具风险等级强制声明。
- 默认只读沙箱。
- 用户可禁用 Skill。

### 风险 3：Web 渲染安全

如果允许模型直接生成 HTML/JS，可能引入安全风险和 UI 不一致。

对策：

- 模型只输出结构化 JSON。
- HTML/CSS/JS 模板由 APP 或受信任 Skill 提供。
- JS bridge 使用白名单 action。
- 写操作必须回到 WPF 安全确认层。

### 风险 4：供应商工具调用差异

不同 LLM 的 tool calling 格式差异大，甚至某些兼容端点不支持。

对策：

- 统一 Tool Call IR。
- Provider Adapter 只做格式翻译。
- 保留 JSON fallback 协议。
- Agent Runtime 不依赖具体 provider。

## 验收标准

### 基础 Agent 能力

1. 用户不进入目标子目录，只输入“分析某目录”，AI 能自动定位并分析该目录。
2. AI 能说明自己查询了哪些工具和结果。
3. AI 不会声称执行了未实际执行的文件操作。
4. 工具调用超限时能给出部分结果和后续建议。

### Skill 机制

1. APP 能从安装目录加载 Skill manifest。
2. Skill 能声明触发规则、工具、UI 模板和安全策略。
3. Skill 能被启用和禁用。
4. 非内置专业能力可以作为 Skill 扩展，而不是写死在主程序。

### Unity Skill

1. 能识别 Unity 项目。
2. 能统计 `Library`、`Temp`、`Obj`、`Logs` 等目录占用。
3. 能读取 Unity Hub 或注册表相关线索。
4. 能区分疑似活跃项目和长期未打开项目。
5. 能给出 `Library` 清理建议和重新导入代价说明。

### SteamLibrary Skill

1. 能识别 Steam 库。
2. 能读取 appmanifest 和 libraryfolders。
3. 能列出游戏、Workshop、残留内容的空间占用。
4. 能给出清理或迁移建议。

### 动态 Web 结果渲染

1. AI 能返回结构化结果和模板 ID。
2. APP 使用固定模板渲染对象卡片/表格。
3. 用户能排序、筛选、勾选对象。
4. 用户动作能通过安全 bridge 回到 WPF。
5. 删除/移动必须经过 WPF 原生确认。

## 结论

本次头脑风暴确认：项目真正应该补的不是“给 prompt 塞更多扫描数据”，而是建立 Agent 化的工具调用体系、Skill 生态和动态结果渲染体系。

最值得优先落地的是：

1. `IFileTreeQueryService`
2. 统一 Tool Call IR
3. Agent Runtime 工具调用循环
4. Skill manifest 与安装目录加载机制
5. Unity 项目清理 Skill
6. SteamLibrary 分析 Skill
7. WebView2 + JSON-driven UI 渲染
8. MCP Adapter 作为后续扩展入口

ComfyUI workflow 反查模型引用关系不纳入当前主程序需求，可作为未来非内置 Skill 示例验证 Skill 生态的扩展能力。
