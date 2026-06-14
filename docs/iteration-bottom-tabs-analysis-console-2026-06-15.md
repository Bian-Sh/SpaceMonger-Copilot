# 迭代纪要：底部 Tab 与分析控制台入口（2026-06-15）

## 背景

用户在点击“推荐清理”后遇到偶发分析失败或返回 0 条推荐。原界面在下方推荐区域显示 `Click Analyze to run AI analysis on your scan results.`，容易被误解为一条路径 item；同时状态栏只展示“分析完成：找到 0 条推荐”，缺少可追踪失败原因和重试线索。

## 改动

- `MainWindow` 底部可拖拽区域从单一 `RecommendationsPanel` 改为 `TabControl`：
  - `推荐清理` Tab：承载原推荐列表。
  - `控制台` Tab：承载分析状态、范围、失败原因、0 推荐提示等运行日志。
- 状态栏 `ScanProgressText` 改为 `Hyperlink` 样式入口：
  - 鼠标划入具备链接视觉特征。
  - 点击后打开底部区域并切换到 `控制台` Tab。
- 分析流程新增 UI 内控制台日志：
  - 分析开始时记录范围。
  - 分析失败时记录失败消息并自动切换控制台。
  - 分析成功但 0 条推荐时记录“AI 返回 0 条推荐”的提示并自动切换控制台。
- 推荐面板空态改为明确提示“未找到可推荐清理项”，不再显示英文 `Click Analyze...`，避免被误认为文件夹路径 item。

## 关键文件

- `src/SpaceMonger.App/MainWindow.xaml`
- `src/SpaceMonger.App/MainWindow.xaml.cs`
- `src/SpaceMonger.App/Views/RecommendationsPanel.xaml`

## 验证建议

1. 完成一次扫描。
2. 点击 `推荐清理`，确认底部区域以 Tab 形式出现。
3. 点击状态栏分析状态文本，确认切换到 `控制台` Tab。
4. 当分析失败时，确认控制台自动打开并展示失败原因。
5. 当分析成功但 0 条推荐时，确认推荐 Tab 显示空态提示，控制台记录 0 推荐说明。
