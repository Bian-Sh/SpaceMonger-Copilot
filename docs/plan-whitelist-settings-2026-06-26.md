# 设置页白名单配置实现计划

## Summary
- 在设置页新增“白名单”配置区，包含“扫描”“清理推荐”“AI 对话”三个独立板块。
- 白名单语义固定为“排除/保护”：扫描跳过、清理推荐不推荐、AI 不暴露/不查询匹配路径。
- 右键复制/粘贴使用 JSON，便于三个板块之间同步数据；每条记录包含 `path` 与可选 `description`。
- 不改动已实现的免责声明板块，也不重复新增免责声明文案。

## Key Changes
- 在 `src/SpaceMonger.Core/Models/AppSettings.cs` 增加三个白名单集合：扫描、清理推荐、AI 对话；新增共享条目类型，字段为 `Path`、`Description`，默认空列表并兼容旧设置文件。
- 新增路径匹配服务：保存用户原始路径；运行时用 `Path.GetFullPath` 规范化，Windows 下大小写不敏感；匹配规则为“路径本身或其子路径”；无效/不存在路径保留在设置中但不参与匹配。
- 设置页 UI 采用类似 Windows 环境变量的列表交互：每个板块支持添加文本路径、`OpenFolderDialog` 选择文件夹、编辑 description、删除、右键复制/粘贴 JSON。
- JSON 剪贴板格式固定为数组：`[{ "path": "C:\\Example", "description": "备注" }]`；粘贴时去重并合并 description；非法 JSON 显示本地化错误，不覆盖现有数据。
- 扫描链路：`FileScanner` 普通枚举和 `IncrementalFileScanner` MFT/USN 快路径都应用扫描白名单；若用户选择的扫描根本身被白名单排除，阻止扫描并显示本地化提示。
- 清理推荐链路：推荐引擎在构造 LLM 输入前过滤白名单路径，并在解析 LLM 返回结果后再次过滤，避免已保护路径出现在推荐列表。
- AI 对话链路：上下文摘要、选中项、推荐关联项和所有只读工具查询都过滤 AI 白名单路径；若用户询问被保护路径，返回“该路径已按设置隐藏”的本地化说明。
- 更新 `src/SpaceMonger.App/Localization/Strings.resx` 与 `src/SpaceMonger.App/Localization/Strings.zh-CN.resx`，新增白名单相关 UI、错误提示和说明文案。
- 按项目约定新增一次迭代纪要到 `docs`，并在实质修改后发布 exe 到 `outputs/yyyyMMdd-HHmm-whitelist-settings`。

## Test Plan
- 新增核心单元测试：路径规范化、大小写不敏感匹配、父子路径匹配、无效路径保留但不匹配、重复粘贴合并。
- 新增扫描测试：普通扫描跳过白名单目录；MFT/USN 树构建后过滤白名单子树；扫描根被排除时返回明确失败状态或 UI 提示。
- 新增推荐测试：白名单路径不会进入 LLM prompt；即使 LLM 返回受保护路径，也会在结果阶段被丢弃。
- 新增 AI 工具测试：列表、按名查找、按路径查找、子树摘要、最大文件查询均不返回 AI 白名单路径。
- 新增设置页 ViewModel 测试：JSON 复制/粘贴、非法 JSON 错误、跨板块粘贴、description 保留。
- 验证命令：先运行相关测试项目，再运行整体 `dotnet test`；最后 `dotnet publish` WPF 应用到 `outputs` 目录。

## Assumptions
- “白名单”按“排除/保护”实现，不做“仅允许列表内路径”的模式。
- 剪贴板只要求 JSON 格式；不额外实现纯文本行格式。
- `description` 仅用于用户备注和 AI 可见的白名单说明，不参与路径匹配。
- 白名单保护是应用层保护，不承诺阻止用户在系统资源管理器或其他工具中删除文件。
- 已实现的免责声明板块保持原样，本计划不触碰其 UI、资源文案和文档。
