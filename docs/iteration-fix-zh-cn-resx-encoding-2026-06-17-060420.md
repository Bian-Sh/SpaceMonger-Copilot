# 迭代纪要：修复 zh-CN 资源乱码（2026-06-17 06-04）

## 背景

导航栏修复过程中，`src/SpaceMonger.App/Localization/Strings.zh-CN.resx` 曾被错误按终端显示内容重写，导致真实 UTF-8 中文变成 mojibake。该问题已立即止血并恢复。

## 修复

- 使用 Git 恢复 `Strings.zh-CN.resx` 原始中文内容。
- 不再依据 PowerShell / `git diff` 的终端显示判断中文是否损坏。
- 通过 Python 以 `utf-8-sig` 读取并用 `unicode_escape` 检查真实字符。
- 仅追加两个必要的新本地化键：
  - `TreemapAnalysisRequiredTitle`：`需要重新分析`
  - `TreemapAnalysisRequiredHint`：`当前位置不在已扫描的树中，请重新分析该文件夹。`

## 验证

- `Strings.zh-CN.resx` XML 解析通过。
- `dotnet build src\SpaceMonger.sln -c Debug` 通过，`0` errors。
- 已重新发布修复后的 Windows x64 folder publish：
  - `outputs\SpaceMonger-win-x64-folder-20260617-060420`
  - `outputs\SpaceMonger-win-x64-folder-20260617-060420\SpaceMonger.App.exe`

## 后续约束

修改中文资源文件时，必须用编码安全方式验证真实内容；禁止因为 PowerShell、终端或 diff 显示乱码而批量重写中文文件。
