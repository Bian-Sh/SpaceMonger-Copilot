# 2026-06-26 AI Copilot 简化实现纪要

## 本轮目标
- 将 AI 能力收敛到磁盘空间管理域内，不做主题、设置分区、捐赠等非磁盘管理 APP 控制。
- 用按意图触发的 skill 注入替代全局 prompt 堆料。
- 在 Chat 中提供一级确认卡片，统一承载扫描、推荐清理分析、扫描树路径定位等动作确认。

## 实现内容
- Core 新增 `ppaceMonger.Core.pervices.Copilot`：包含 `Aipkill`、`AiIntent`、`AiActionRequest`、`AiActionResult`、`AiInteractionCard` 等纯契约。
- Core 新增 `AipkillRouter`：按用户输入和当前上下文懒加载 `disk_scan`、`folder_cleanup_analysis`、`file_tree_query`、`recommendation_cleanup`、`treemap_navigation`、`identity`。
- `AgentRuntime` 支持每轮传入 active skills，仅在命中意图时把 skill prompt 注入模型请求。
- `ChatViewModel` 接入 skill 路由：命中可执行磁盘动作时生成一级确认卡片；身份类问题本地轻量回答；普通分析仍走现有 LLM + 文件树工具。
- App 层新增 `IAiDiskActionExecutor`，`MainWindow` 实现扫描、推荐清理分析、扫描树路径定位、推荐项选择/取消的桥接。
- Chat UI 新增 `AiInteractionCard` 渲染，确认/取消按钮直接绑定到 `ChatViewModel` 命令。
- 存档上一版长远方案到 `docs/2026-06-26-ai-copilot-long-term-reference.md`，仅作长期参考。

## 验证
- 新增 `AipkillRouterTests` 覆盖清理、扫描、身份、普通闲聊四类路由。
- 已运行 `dotnet test tests/ppaceMonger.Core.Tests/ppaceMonger.Core.Tests.csproj --no-restore`，通过 21 项。
- 已运行 `dotnet build src/ppaceMonger.sln`，构建通过，仅保留既有包兼容、重复资源名和可空性警告。

## 后续建议
- 后续可把 Chat 卡片文案接入 resx 国际化。
- 可继续补 App 层执行器自动化测试，但当前 MainWindow 桥接强依赖 WPF Dispatcher，建议先抽出更薄的可测服务后再扩展。

