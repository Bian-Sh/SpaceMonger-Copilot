# 迭代纪要：推荐清理 API envelope 不显示原因诊断（2026-06-26）

## 背景

用户提供两份 `SpaceMonger.Next` 本地缓存的 AI 分析响应 envelope：

- `api-envelope-20260626-082658-814.json`：AI 返回了推荐项，但推荐清理 ScrollView 没有展示。
- `api-envelope-20260626-083420-769.json`：AI 返回的推荐项可以展示到推荐清理 ScrollView。

## 诊断结论

这不是 ScrollView 绑定或 UI 铺列表失败，而是推荐分析后处理阶段把第一份响应里的所有推荐项过滤掉了。

当前流程：

1. `RecommendationsViewModel.AnalyzeAsync()` 调用 `IRecommendationEngine.AnalyzeWithDiagnosticsAsync()`。
2. `RecommendationEngine.ParseResponse()` 能从 AI 文本中解析出 `recommendations` 数组。
3. 如果不是聚焦子路径分析（`focusEntry == null`），引擎会执行全盘分析保护过滤：
   - `RecommendationEngine.cs` 中 `Where(r => !IsProtectedPath(r.TargetPath))`
   - `IsProtectedPath()` 会把包含 `Windows`、`Program Files`、`Program Files (x86)` 的路径视为受保护系统路径。
4. 第一份 envelope 里的 20 条路径全部位于 `C:\Windows\assembly\...`，因此全被过滤，最终 `result.Recommendations.Count == 0`，UI 没有可展示项。
5. 第二份 envelope 也有 20 条，但只有 4 条受保护路径被过滤，剩余 16 条位于用户缓存、NVIDIA cache、Chrome cache、NuGet、Gradle 等路径，因此能展示。

## 对比统计

### api-envelope-20260626-082658-814.json

- `stop_reason`: `end_turn`
- 原始 AI 推荐数：20
- 被 `IsProtectedPath()` 过滤：20
- 最终可展示：0
- 主要路径：`C:\Windows\assembly\temp`、`C:\Windows\assembly\NativeImages_v4.0.30319_*\...`

### api-envelope-20260626-083420-769.json

- `stop_reason`: `end_turn`
- 原始 AI 推荐数：20
- 被 `IsProtectedPath()` 过滤：4
- 最终可展示：16
- 可展示路径示例：
  - `C:\Users\BianShanghai\AppData\Local\Temp`
  - `C:\Users\BianShanghai\AppData\Local\NVIDIA\DXCache`
  - `C:\Users\BianShanghai\AppData\Local\Google\Chrome\User Data\Default\Cache`
  - `C:\Users\BianShanghai\.gradle\caches`
  - `C:\ProgramData\NVIDIA Corporation\NVIDIA App\UpdateFramework\ota-artifacts`

## 相关代码位置

- `src/SpaceMonger.Core/Services/Analysis/RecommendationEngine.cs`
  - `AnalyzeWithDiagnosticsAsync()` 中区分全盘分析和聚焦路径分析。
  - 全盘分析时会过滤 `IsProtectedPath()` 命中的推荐项。
- `src/SpaceMonger.Core/Services/Analysis/RecommendationEngine.PathSafety.cs`
  - `IsProtectedPath()` 定义受保护路径规则。
- `src/SpaceMonger.App/MainWindow.Console.cs`
  - 只有当 `TreemapViewModel.CurrentRoot != CurrentSession.RootEntry` 时才传入 `focusEntry`。
  - 聚焦子路径分析下不会静默丢弃系统路径，而是把 `Safe` 降级为 `ReviewFirst`。

## 后续可选优化

如果希望用户更容易理解“AI 返回了但没显示”，可以在 `RecommendationsViewModel` 或分析完成提示中显示诊断信息，例如：

- 当 `ParsedRecommendationCount > 0` 且 `Recommendations.Count == 0` 且 `ProtectedFilteredCount > 0` 时，提示“AI 返回的推荐均位于受保护系统路径，已被安全策略隐藏”。
- 在控制台追加 `ProtectedFilteredCount`、`MissingEntryCount` 等诊断计数。
- 如果用户明确钻入 `C:\Windows\assembly` 后点“推荐清理”，保持现有聚焦分析策略：不隐藏，但降级安全等级并提示谨慎。
