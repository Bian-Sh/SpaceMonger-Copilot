# 2026-06-19 设置页验证状态符号与颜色修复

## 问题

RefreshValidationStatusText() 方法在资源字符串已有 ✓/✗ 符号的情况下，又用 Unicode 字符拼了一次，导致双重符号。

## 修复

- SettingsViewModel.cs: RefreshValidationStatusText() 改为直接使用 L.Text(...) 获取本地化文本，不再额外拼接符号
- 资源文件已包含符号: 英语 ✓ Valid / ✗ Invalid，中文 ✓ 有效 / ✗ 无效
- 颜色通过 XAML DataTrigger: Valid → VP.SuccessBrush (#30D158 绿), Invalid → VP.DangerBrush (#FF453A 红)

## 验证

- dotnet build -c Release 通过
- 发布到 outputs\SpaceMonger-win-x64-folder-20260619-173826

