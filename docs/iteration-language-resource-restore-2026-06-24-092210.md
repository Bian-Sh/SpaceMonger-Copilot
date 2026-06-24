# 多语言资源恢复 - 2026-06-24-092210

## 根因
- 实际检查发现 settings.dat 中 Language 已保存为 zh-CN，但主界面仍显示英文。
- 直接读取资源文件后确认：Strings.zh-CN.resx 中大量主界面 key 已被英文化，例如 ScanButton、TreemapTabHeader、SettingsTitle 等。

## 变更内容
- 从 git 历史恢复 47 个主界面 zh-CN 资源 key 的中文值。
- 保留历史 LocExtension 手动刷新模型与语言偏好持久化修复。

## 验证
- 已确认 ScanButton=扫描、TreemapTabHeader=树状图、SettingsTitle=设置。
- 已执行 dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj，结果通过；仅有既有 warning。
- 已执行 dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o outputs/language-resource-restore-2026-06-24-092210。

## 输出
- 发布目录：outputs/language-resource-restore-2026-06-24-092210
