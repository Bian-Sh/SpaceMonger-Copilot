# 发现记录

- 2026-06-28 18:22:30 +08:00 用户反馈：分析 C 盘后点击“分析”，预期进入分析进度展示页，随后 APP 崩溃。
- 2026-06-28 18:27:53 +08:00 崩溃日志定位到 WPF 跨线程访问：MainViewModel_PropertyChanged 在非 UI 线程调用 TreemapView.SetScanningState，异常为调用线程无法访问由另一线程拥有的对象。

- 2026-06-29 12:30:43 +08:00 最新反馈：fixture 不崩，但真实 C 盘扫描完成后点击分析仍崩。排查到 RecommendationEngine.Metadata 仍会递归拉平全盘文件/目录，并在指纹/Unity 发现中递归遍历，C 盘百万节点下存在 OOM/StackOverflow/卡死风险。
