# 进度记录

- 初始化计划：准备梳理仓库结构与现有 UI 实现。
- apply_patch.bat 在当前 PowerShell 环境返回 Access is denied，改用受控脚本做等价文本补丁。
- 已完成：`@` skill 菜单、鼠标选择、上下/Tab/Enter 确认逻辑。
- 已完成：路由层支持 `@skill-id` 强制注入 app skill prompt，并向模型提示“用户显式选择 skill”。
- 已完成：选中 skill 的模型调用会触发 workflow step 展示器；cua 验证显示 `第 1/2 步` 且应用不再崩溃。
- 已修复：step 旋转 Storyboard 在 WPF 中触发不可变对象动画崩溃，改为稳定显示运行图标。
- 验证：`dotnet test src/SpaceMonger.sln` 通过，Core 53 / App 14。
- 发布：`outputs/spacemonger-copilot-2026-06-27-214640/`。
- 记录：codegraph explore --max 不支持，且 g 正则包含裸 ???? 导致解析失败；改用简单查询。
- 记录：误用 bash heredoc 在 PowerShell 中执行 Python 失败；改用 PowerShell here-string。
- 记录：PowerShell 下 g path* 被当成非法路径，改为搜目录。
- 已修复：selected skill workflow 的中文文案不再是 `????`，改为 C# Unicode 转义避免 PowerShell 编码污染。
- 已调整：`@` 菜单项左侧增加蓝色小圆圈；step 胶囊圆角从 14 调整为 10，运行态圆点保留蓝色圆环。
- 已增强：RecommendationEngine 对 Unity 项目结构（同级存在 Assets + ProjectSettings）的 Library 目录增加本地推荐兜底，避免普通 Library 误报。
- 验证：`dotnet test src/SpaceMonger.sln` 通过，Core 55 / App 14。
- 发布：`outputs/spacemonger-copilot-2026-06-27-215724/`。
- CUA 验证：创建 `outputs/cua-unity-sample`，启动发布包，选择并扫描样本，`@` 菜单显示 3 项，选择 `@unity-project-cleanup` 后发送请求，显示 step `第 1/2 步` 与“加入推荐列表”确认卡，确认后推荐列表出现 `...\Library`。
- 记录：`capture_window` 截图工具调用失败，改以 CUA `get_window_state` UIA 树确认状态。
- 最终：测试实例已关闭，避免锁住发布包。
