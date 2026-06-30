# 发现记录

- 截图中的 gap 来自底部工具窗 tab（推荐清理 / 控制台），使用共享 `VP.TabButton` 的 `Padding="14,7"`。
- 共享样式也用于顶部视图 tab；为避免连带影响，只在底部两个 `RadioButton` 局部覆盖为 `Padding="10,7"`。
- cua-driver 验证中，底部 tab frame 为 `x=1128,w=73` 与 `x=1201,w=59`，首尾相接，视觉 gap 已缩小。
