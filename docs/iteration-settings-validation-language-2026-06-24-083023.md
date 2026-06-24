# 设置页验证状态与全局多语言修复 - 2026-06-24-083023

## 变更内容
- 修复验证按钮后的状态文案资源乱码，恢复为绿色 ✓ Valid / 有效 与红色 ✕ Invalid / 无效。
- 修复 zh-CN 验证中、API Key 无效、API Key 为空等提示文案。
- 语言设置保存时立即调用 L.SetLanguage，并在 Language 属性变化时同步触发全局语言切换。
- 修正 LocExtension：对依赖属性返回真正的 Binding，LanguageChanged 后可驱动全 app 使用 loc:Loc 的控件刷新。

## 验证
- 已用脚本确认验证资源值为 Unicode ✓ / ✕，不是 mojibake 字符。
- 已执行 dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj，结果通过；仅有既有 NuGet/nullable/resource 警告。
- 已执行 dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o outputs/settings-validation-language-2026-06-24-083023。

## 输出
- 发布目录：outputs/settings-validation-language-2026-06-24-083023
