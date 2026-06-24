# 弹窗与关于页中文资源恢复 - 2026-06-24-093248

## 根因
- 多个非设置页弹窗、关于页、投喂弹窗对应的 zh-CN 资源值被英文化。
- 语言切换到 zh-CN 后仍显示英文，是因为中文卫星资源本身返回英文。

## 变更内容
- 从 git 历史恢复弹窗、清理确认、无扫描数据、关闭确认、关于页、投喂弹窗、更新状态、聊天/树表头等 zh-CN 文案。
- 修复 NoItemsSelectedForCleanupToolTip 重复资源项，消除重复资源 warning。
- 审计 Title/Message/Button/Label/Header/Description/ToolTip/Text/Status/Dialog 等疑似 UI 文案键，剩余英文残留为 0。

## 验证
- 已执行 zh-CN 英文残留审计：remaining 0。
- 已执行 dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj，结果通过；仅有既有 NuGet warning。
- 已执行 dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o outputs/dialog-about-zh-restore-2026-06-24-093248。

## 输出
- 发布目录：outputs/dialog-about-zh-restore-2026-06-24-093248
