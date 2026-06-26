# 迭代纪要：设置页白名单配置

时间：2026-06-26

## 本次实现
- 在设置页新增“白名单”分区，拆分为“扫描”“清理推荐”“AI 对话”三个独立列表。
- 白名单语义统一为排除/保护：扫描跳过、清理推荐不推荐、AI 对话不查询也不暴露匹配路径。
- 每条白名单记录包含 `Path` 与可选 `Description`，并持久化到应用设置。
- 支持添加文本路径、选择文件夹、编辑备注、删除，以及右键复制/粘贴 JSON 数组。
- 新增路径匹配器：运行时按 `Path.GetFullPath` 规范化，Windows 下大小写不敏感，匹配路径自身或子路径；不存在/无效路径保留但不参与匹配。

## 链路接入
- `FileScanner` 普通扫描枚举阶段跳过扫描白名单。
- `IncrementalFileScanner` 在 MFT 快速扫描树构建后剪枝白名单子树，并在扫描根被排除时阻止扫描。
- `RecommendationEngine` 在构造 LLM 输入前过滤清理推荐白名单，并在解析返回结果后再次过滤。
- `FileTreeQueryService` 与只读 Agent 工具按 AI 对话白名单隐藏路径，直接查询隐藏路径时返回隐藏错误。

## 验证
- 新增 `PathWhitelistMatcherTests` 覆盖父子路径匹配、Windows 大小写语义、无效/不存在路径不匹配、粘贴合并去重。
- 已运行 `dotnet test src\\SpaceMonger.sln`，全部通过；保留现有第三方包兼容性与既有资源重复警告。

## 输出
- WPF 发布包按约定输出到 `outputs/20260626-*-whitelist-settings`。
