# 迭代纪要：分析按钮不再跳转控制台（2026-06-23）

## 背景

用户反馈点击底部推荐清理面板的 `分析` 按钮后，界面会自动跳转到 `控制台` Tab，不符合预期。用户希望点击分析后继续停留在推荐清理视图，便于观察加载态、空态、错误态或推荐结果。

## 根因

`MainWindow.Console.cs` 的 `OnAnalyzeRequested()` 在分析流程结束后存在两处强制切换：

- 分析失败时调用 `ShowConsolePanel()`。
- 分析成功但推荐数为 0 时调用 `ShowConsolePanel()`。

这会覆盖分析开始时已经执行的 `ShowRecommendationsPanel()`，导致用户点击 `分析` 后被带到控制台。

## 修改

- 移除分析失败后的自动 `ShowConsolePanel()`。
- 移除 0 条推荐诊断场景下的自动 `ShowConsolePanel()`。
- 保留 `AppendConsoleLine()` 和 `AppendAnalysisDiagnostics()`，诊断信息仍写入控制台日志，用户需要时可手动切换到 `控制台` 查看。

## 影响

- 点击 `分析` 后界面保持在 `推荐清理` Tab。
- 错误和 0 推荐场景不再抢焦点/跳转 Tab。
- 控制台诊断能力保持不变。
