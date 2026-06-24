# 多语言实时切换与持久化修复 - 2026-06-24-085450

## 变更内容
- 修复语言下拉只改 UI 状态、未稳定写入 settings.dat 的问题：Language 属性变化后立即保存语言偏好。
- LoadSettings 期间增加 _isLoadingSettings 防护，避免打开设置页时反向触发保存。
- 修复 LocExtension 实时刷新：为每个目标控件保留 LocBinding 引用，避免绑定源被 GC 回收后 LanguageChanged 无法驱动页面刷新。
- 保留启动时 App.xaml.cs 从 settings.dat 读取语言并调用 L.SetLanguage 的路径，因此重启后会使用保存语言。

## 验证
- 已执行 dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj，结果通过；仅有既有 NuGet/nullable 警告。
- 已执行 dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o outputs/language-realtime-persist-2026-06-24-085450。

## 输出
- 发布目录：outputs/language-realtime-persist-2026-06-24-085450
