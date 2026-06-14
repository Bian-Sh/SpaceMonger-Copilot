# 迭代纪要：内嵌设置页、模型参数与 Treemap 空态（2026-06-15）

## 背景

用户希望设置不再使用弹窗，而是类似工具设置页的左右分栏页面；同时需要能配置大模型名称、thinking 开关和语言。推荐清理应按 App 当前语言生成说明。Treemap 在未扫描前的黑色区域应显示“无数据”，扫描遮罩也只覆盖 Treemap 黑区，不应遮住左侧整个工作区。

## 改动

- 新增 `SettingsPage` 内嵌设置页面：
  - 左侧导航栏、右侧设置内容布局。
  - `设置` 按钮和聊天面板的“打开设置”都切换到内嵌页面，不再弹窗。
- 扩展设置项：
  - `AnalysisModelName`：推荐分析模型名。
  - `ChatModelName`：聊天模型名。
  - `EnableThinking`：是否允许 thinking。
  - `Language`：App 语言与 prompt localization 声明。
- 推荐分析请求：
  - 读取设置中的分析模型名和 thinking 开关。
  - prompt 末尾加入 localization 声明，要求 explanation 与 App 当前语言一致。
  - DeepSeek endpoint 默认禁用 thinking；若用户开启则不发送 disabled thinking。
- 诊断日志：
  - 保留 API envelope、raw response、stop reason。
  - 如果响应中存在 thinking/reasoning 字段，写入 `logs/thinking/` 并在控制台输出路径。
- Treemap：
  - 未扫描时在黑色区域显示“无数据”。
  - 扫描遮罩从主窗口左侧整区移动到 Treemap 黑色绘图区内部。

## 验证方式

- `dotnet build .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Debug`
- `dotnet test .\src\SpaceMonger.sln -c Debug --no-restore`

