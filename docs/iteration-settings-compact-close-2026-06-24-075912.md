# 设置页紧凑化与关闭按钮恢复 - 2026-06-24-075912

## 变更内容
- 将设置面板最大尺寸从 780x680 收敛到 720x580，左侧导航列同步缩窄。
- 恢复 git 历史中的右上角圆形关闭按钮，保持悬停红色反馈。
- 保留右侧单一 ScrollViewer 与左侧导航联动逻辑。
- 将 Anthropic 设置区标题改为 AI 设置 / AI Settings。
- 复核 AppSettings：当前持久化设置项已覆盖，LastScanPath 属于运行状态，不新增到设置页。

## 验证
- 已执行 dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj，结果通过；仅有既有 NuGet/nullable/resource 警告。
- 已执行 dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o outputs/settings-compact-close-2026-06-24-075912。

## 输出
- 发布目录：outputs/settings-compact-close-2026-06-24-075912
