# Findings


## 2026-07-01
- 控制台原实现是 `TextBlock` + colored `Run`，WPF `TextBlock` 默认不可选择。
- 为避免丢失原日志配色，最终方案不是直接替换为可见 `TextBox`，而是在原 `TextBlock` 上叠透明只读 `TextBox`。
- 复制按钮直接从 `UiLogEntry.DisplayText` 复制完整条目，避免依赖当前文本选择范围。
