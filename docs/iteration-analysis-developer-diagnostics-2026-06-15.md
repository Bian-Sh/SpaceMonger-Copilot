# 迭代纪要：分析控制台开发者诊断日志（2026-06-15）

## 背景

用户测试时看到控制台仅输出“分析完成：找到 0 条推荐”和泛泛的重试提示，无法判断 0 条推荐是模型真的返回空数组、响应无法解析、字段缺失、路径无法匹配，还是后处理过滤导致。控制台应面向开发者，输出可直接定位问题的诊断信息。

## 改动

- Core 层新增 `AnalysisDiagnostics` / `AnalysisResult`：
  - 记录扫描目标、分析 scope、是否 focused scope。
  - 记录 metadata 字符数、LLM 原始响应字符数、提取出的 JSON 字符数。
  - 记录解析出的推荐数、被系统路径过滤数、路径未匹配数、单条格式错误数、缺字段数。
  - 记录 parse error、原始响应 preview 或 extracted JSON preview。
- `IRecommendationEngine` 新增 `AnalyzeWithDiagnosticsAsync(...)`，原 `AnalyzeAsync(...)` 保持兼容并委托到诊断接口。
- `RecommendationsViewModel` 保存 `LastDiagnostics`，供 UI 控制台读取。
- `MainWindow` 控制台输出从用户提示升级为 `DIAG:` 开头的开发者诊断日志：
  - 分析失败时输出诊断上下文。
  - 分析成功但 0 条推荐时输出 parsed/filter/parse error 统计，帮助判断原因。

## 关键文件

- `src/SpaceMonger.Core/Models/AnalysisDiagnostics.cs`
- `src/SpaceMonger.Core/Models/AnalysisResult.cs`
- `src/SpaceMonger.Core/Services/Analysis/IRecommendationEngine.cs`
- `src/SpaceMonger.Core/Services/Analysis/RecommendationEngine.cs`
- `src/SpaceMonger.App/ViewModels/RecommendationsViewModel.cs`
- `src/SpaceMonger.App/MainWindow.xaml.cs`

## 测试关注点

1. 点击 `推荐清理`。
2. 若结果为 0，打开控制台查看：
   - `parsed_recs=0` 且有 `parse_error`：优先看模型输出格式或响应截断。
   - `parsed_recs>0` 且 `protected_filtered>0`：说明后处理过滤掉了系统/受保护路径。
   - `missing_entry>0`：说明模型返回路径与扫描树路径不完全一致。
3. 若失败，查看 `response_preview` / `extracted_json_preview` 判断 endpoint 返回内容。
