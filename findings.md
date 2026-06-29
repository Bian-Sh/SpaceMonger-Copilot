# 发现记录

- 2026-06-29 13:32:13 +08:00：用户报告完整分析 C 盘后点击“分析”按钮应用瞬间卡死。
- 2026-06-29 13:39:39 +08:00：根因定位为 RecommendationsViewModel.AnalyzeAsync 在 UI 线程调用 RecommendationEngine.AnalyzeWithDiagnosticsAsync；该方法在首个 wait 前同步执行 BuildCompactMetadata，完整 C 盘会遍历整个扫描树并生成 fingerprints，导致窗口消息泵瞬间阻塞。
- 2026-06-29 13:39:39 +08:00：额外发现 RecommendationEngine.IsCleanupExcluded 在元数据遍历中每次调用都会 LoadSettings()，完整 C 盘节点数大时会放大卡顿。
