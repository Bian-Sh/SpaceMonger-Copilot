# 设置页本地化与滚动修正 - 2026-06-24-081328

## 变更内容
- 补齐设置页使用的本地化 key，修复 !SettingsBaseUrlLabel!、!ValidateButtonLabel! 这类占位文本。
- 将 zh-CN 设置页相关文案从英文补回中文，包括常规、主题、模型、语言、清理方式、毛玻璃等设置项。
- 移除右侧 Settings 标题，让右侧内容从设置区块标题开始。
- 下移右侧 ScrollViewer 内容区域，避免内容顶到右上关闭按钮下方。
- 增加 BottomScrollSpacer，并按视口高度动态调整，确保点击 Theme 时标题能贴齐滚动区域上沿。

## 验证
- 已执行本地化 key 检查：SettingsPage.xaml 使用的 loc key 在 en/zh-CN resx 中均存在。
- 已执行 dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj，结果通过；仅有既有 NuGet/nullable/resource 警告。
- 已执行 dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o outputs/settings-i18n-scroll-spacer-2026-06-24-081328。

## 输出
- 发布目录：outputs/settings-i18n-scroll-spacer-2026-06-24-081328
