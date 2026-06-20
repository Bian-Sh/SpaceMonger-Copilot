# 迭代纪要：Chat Markdown 渲染和 Thinking 模式（2026-06-20）

## 背景

用户要求实现 Chat 窗口的 Markdown 渲染和 AI thinking 模式的处理，参考 opencode 的实现方式。

## 修改内容

### 1. Markdown 渲染

- 添加了 Markdig NuGet 包（v0.37.0）用于 Markdown 解析
- 创建了 `MarkdownToFlowDocumentConverter.cs` - 将 Markdown 文本转换为 WPF FlowDocument
- 支持的 Markdown 语法：
  - 标题（H1-H4）
  - 段落
  - 粗体、斜体
  - 行内代码（带背景色）
  - 代码块（带语法高亮背景）
  - 有序/无序列表
  - 引用块
  - 链接
  - 水平分割线

### 2. Thinking 模式处理

- 更新了 `ChatMessage.cs` - 添加 `Thinking`、`IsThinkingExpanded`、`HasThinking` 属性
- 更新了 `ILlmClient.cs` - 添加 `ChatResponse` record 和 `StreamChatWithThinkingAsync` 方法
- 更新了 `AnthropicClient.cs` - 实现 `StreamChatWithThinkingAsync`，支持流式 thinking/text 分离
- 更新了 `IChatService.cs` 和 `ChatService.cs` - 添加 `StreamMessageWithThinkingAsync` 方法
- 更新了 `ChatViewModel.cs` - 使用新的 thinking 流式方法，添加 `ToggleThinking` 命令

### 3. UI 更新

- 更新了 `ChatPanel.xaml`：
  - 消息文本使用 `FlowDocumentScrollViewer` 渲染 Markdown
  - 添加了可折叠的 thinking 区域（橙色背景，点击展开/折叠）
  - 添加了 thinking 图标和标签
- 创建了 `BoolToVisibilityConverter.cs` - 布尔值到可见性转换器

### 4. 本地化

- 更新了 `Strings.resx` 和 `Strings.zh-CN.resx`：
  - 添加了 `ThinkingLabel`（思考中 / Thinking）
  - 添加了 `ThinkingText`（思考中... / Thinking...）

## 文件列表

**新增文件**：
- `src/SpaceMonger.App/Converters/MarkdownToFlowDocumentConverter.cs`
- `src/SpaceMonger.App/Converters/BoolToVisibilityConverter.cs`

**修改文件**：
- `src/SpaceMonger.App/SpaceMonger.App.csproj` - 添加 Markdig 包
- `src/SpaceMonger.Core/Models/ChatMessage.cs` - 添加 thinking 相关属性
- `src/SpaceMonger.Core/Services/Llm/ILlmClient.cs` - 添加 ChatResponse 和新方法
- `src/SpaceMonger.Core/Services/Llm/AnthropicClient.cs` - 实现 thinking 流式处理
- `src/SpaceMonger.Core/Services/Chat/IChatService.cs` - 添加新方法
- `src/SpaceMonger.Core/Services/Chat/ChatService.cs` - 实现 thinking 流式处理
- `src/SpaceMonger.App/ViewModels/ChatViewModel.cs` - 集成 thinking 功能
- `src/SpaceMonger.App/Views/ChatPanel.xaml` - Markdown 渲染和 thinking UI
- `src/SpaceMonger.App/Views/ChatPanel.xaml.cs` - thinking 点击事件
- `src/SpaceMonger.App/Localization/Strings.resx` - 英文本地化
- `src/SpaceMonger.App/Localization/Strings.zh-CN.resx` - 中文本地化

## 验证

- 已运行 `dotnet build src\SpaceMonger.sln`，构建成功
- 已发布 `win-x64` folder 版到 `outputs\SpaceMonger-markdown-thinking-20260620`

## 发布产物

- `outputs/SpaceMonger-markdown-thinking-20260620/SpaceMonger.App.exe`
- 该发布方式是 folder publish，需要整个目录一起分发

## 参考实现

参考了 opencode 项目的实现方式：
- `packages/ui/src/components/markdown.tsx` - Markdown 渲染组件
- `packages/tui/src/context/thinking.ts` - Thinking 模式管理
- `packages/tui/src/routes/session/index.tsx` - ReasoningPart 渲染