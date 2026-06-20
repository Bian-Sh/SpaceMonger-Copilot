# 2026-06-19 设置页校验状态乱码修复

## 问题

设置页 Anthropic API Key 校验成功/失败状态在中英文环境下显示乱码，例如英文资源中的 鉁?Valid。

## 修复

- 将英文 SettingsValidationValid 从损坏字符改为 Valid。
- 将英文 SettingsValidationInvalid 从损坏字符改为 Invalid。
- 将中文 SettingsValidationValid 改为 有效。
- 将中文 SettingsValidationInvalid 改为 无效。
- 去掉符号图标依赖，避免字体或编码环境差异导致再次显示异常。

## 验证

- dotnet build src\\SpaceMonger.App\\SpaceMonger.App.csproj -c Release 成功。
- 已发布到 $(D:\AppData\Visual Studio\Projects\spacemonger-next\outputs\SpaceMonger-win-x64-folder-20260619-164857.Replace((Get-Location).Path + '\\',''))。
