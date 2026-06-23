# 迭代纪要：投喂窗口退出动效统一

时间：2026-06-23 13:32

**背景**：DonateDialog 点击关闭按钮直接 `Close()` 瞬间消失，与 AppModalHost 的淡出+缩放动画不统一。

**修改**：为 `DonateDialog` 新增 `AnimateOut()`，关闭改为：遮罩淡出 + 卡片缩放到 0.9，动画结束后再 `Close()`。

