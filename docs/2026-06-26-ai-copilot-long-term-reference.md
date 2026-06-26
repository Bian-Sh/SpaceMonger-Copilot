# AI Copilot 长远架构参考方案

> 本文档为 2026-06-26 讨论形成的长期参考方案，不作为本轮实现计划。本轮实施以“磁盘空间管理 AI Copilot 简化计划”为准。

## pummary
- 将现有右侧 `ChatPanel` 升级为“APP 共驾助手”：既能解释磁盘扫描结果，也能理解当前页面、设置、主题、推荐清理、关于页和项目/作者信息。
- 保留现有 `Chatpervice`、`AgentRuntime`、文件树只读工具链，在其上增加“APP 状态快照 + 动作计划 + 受控执行器”三层。
- 默认采用“确认后执行”：AI 可生成扫描、打开设置、切换主题、触发推荐分析等操作卡片；真正执行前由用户确认，删除/清理类动作永远二次确认。
- Core 层继续保持可测试、无 WPF 依赖；WPF 层负责把 AI 的 `AppAction` 映射到现有 ViewModel/Window 方法。

## Key Interfaces
- 在 Core 新增 AI 编排契约：`Apppnapshot`、`AppAction`、`ActionPlan`、`ActionResult`。
- 在 App 层新增桥接接口：`IApppnapshotProvider`、`IAppActionExecutor`、`IAppCapabilityCatalog`。

## Implementation Notes
- 扩展 `AgentContext`，加入应用快照、页面状态、设置摘要、主题摘要、关于/项目信息与能力清单。
- 把项目/作者/关于页、投喂/捐赠入口、设置页分区、主题能力、推荐清理流程写成能力卡片注入 prompt。
- 新增“提案型 APP 工具”，模型请求动作时只产出 `ActionPlan`，由 UI 展示确认卡片。
- Chat 面板增加能力起手式、动作卡片和执行轨迹。

## Deferred Reason
- 该方案覆盖全 APP 感知和控制，prompt 成本高，且包含大量非磁盘空间管理能力。
- 本轮改为 skill 懒加载，只实现磁盘空间管理域内 Copilot，并按需回答身份信息。

