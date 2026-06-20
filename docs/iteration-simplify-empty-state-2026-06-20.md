# 迭代纪要：Treemap 空态提示简化（2026-06-20）

## 背景

根据截图反馈，Treemap 区未扫描状态的标题过长，简化成“待扫描”并同步处理多国语言资产。

## 修改内容

- 英文 `Strings.resx`：`TreemapEmptyTitle` value 从 `"Select a drive and start scanning"` 改为 `"Awaiting Scan"`。
- 中文 `Strings.zh-CN.resx`：`TreemapEmptyTitle` value 从 `"选择磁盘并开始扫描"` 改为 `"待扫描"`。
- 其余 treemap 空态 Hint 保持不变。

## 验证

- `dotnet build src/SpaceMonger.sln -c Release`：通过，0 错误；保留既有 `NU1701` 与重复资源名警告。
- `dotnet publish ... -o outputs\spacemonger-next-emptytext-20260620-114411`：通过。

## 输出

- 发布目录：`outputs\spacemonger-next-emptytext-20260620-114411`
