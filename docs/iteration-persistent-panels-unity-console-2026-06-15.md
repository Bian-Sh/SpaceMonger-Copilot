# 迭代纪要：常驻面板与 Unity 风格控制台（2026-06-15）

## 背景

用户希望界面更接近 Unity / Visual Studio 工具窗口：Treemap、底部推荐/控制台、右侧聊天都应常驻显示，并通过 splitter 调整区域，而不是只有扫描、分析或点击聊天后才出现。控制台需要右侧菜单切换 log level，并且日志按时间落盘，便于开发者追踪分析失败与 0 推荐问题。

## 改动

- 主界面布局改为常驻三块：
  - 左上 Treemap 区一直显示。
  - 左下 `TabControl` 一直显示，包含 `推荐清理` 与 `控制台`。
  - 右侧 Chat 一直显示，并通过垂直 `GridSplitter` 左右调整宽度。
- 底部 Treemap / Tab 区域之间的横向 splitter 默认显示，仍可上下调整。
- 控制台新增 Unity 风格 `Log Level` 菜单：`Verbose` / `Info` / `Warning` / `Error`。
- 控制台日志新增内存过滤与文件落盘：
  - UI 根据当前 log level 过滤展示。
  - 完整日志写入 `%LOCALAPPDATA%\SpaceMonger.Next\logs\console-yyyyMMdd-HHmmss.log`。
- Chat 初始化时立即刷新 API key 状态，避免未扫描或扫描过程中误显示 provider 未配置。
- Chat 未扫描时发送消息会提示“请先完成扫描，聊天需要当前磁盘分析上下文”，不再混淆为 API provider 问题。

## 关键文件

- `src/SpaceMonger.App/MainWindow.xaml`
- `src/SpaceMonger.App/MainWindow.xaml.cs`
- `src/SpaceMonger.App/ViewModels/ChatViewModel.cs`

## 验证建议

1. 启动 APP，确认 Treemap、底部 Tab、右侧 Chat 都默认可见。
2. 拖动左右 splitter，确认 Chat 宽度可调整。
3. 拖动上下 splitter，确认 Treemap 与底部 Tab 高度可调整。
4. 在控制台 `Log Level` 菜单切换级别，确认低级别日志被过滤。
5. 未扫描时在 Chat 输入消息，确认提示需要先扫描，而不是 API provider 未配置。
6. 检查 `%LOCALAPPDATA%\SpaceMonger.Next\logs\` 下是否生成按时间命名的日志文件。
