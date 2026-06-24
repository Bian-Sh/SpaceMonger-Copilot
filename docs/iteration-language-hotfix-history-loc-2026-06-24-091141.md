# 多语言热修：恢复历史 LocExtension 刷新模型 - 2026-06-24-091141

## 变更内容
- 回退上一轮错误方向：不再让 loc:Loc 返回 WPF Binding。
- 恢复 git 历史中可用的手动 SetValue 刷新模型，LanguageChanged 后直接更新目标依赖属性。
- 在历史模型基础上保留 LocBinding keep-alive，避免标记扩展对象被回收导致事件丢失。
- 保留语言属性变化即保存到 settings.dat 的持久化修复。

## 验证
- 已执行 dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj，结果通过；仅有既有 NuGet/nullable 警告。
- 已执行 dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o outputs/language-hotfix-history-loc-2026-06-24-091141。

## 输出
- 发布目录：outputs/language-hotfix-history-loc-2026-06-24-091141
