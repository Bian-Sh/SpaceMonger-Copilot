# Progress

- 2026-06-30: 开始处理 chat /new 会话分割展示。

- 2026-06-30: 已实现 /new 保留历史消息、清空上下文、插入系统分割说明；ChatViewModelProposalTests 通过。
- 2026-06-30: 按反馈将 /new 分割说明改为文字左对齐，横线位于文字下方并跨满聊天面板宽度。XAML XML 解析通过；完整 dotnet test 被当前工作区无关 Core override 编译错误阻塞。
- 2026-06-30: 增加消息级异步状态计时：运行时显示已处理秒数，完成后显示操作完成耗时；单步 workflow 不再显示底部 step 指示器。App build 与 ChatViewModelProposalTests 通过。
