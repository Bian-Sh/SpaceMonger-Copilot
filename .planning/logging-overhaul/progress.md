# 进度记录

- 已把 app/core 日志迁移到 Serilog + Microsoft.Extensions.Logging。
- 已添加 UI sink，将 Serilog 事件同步到 app 控制台列表展示。
- 已替换旧 TextBox 控制台和手写文件追加日志实现。
- 已补充启动、退出、窗口生命周期、扫描、增量扫描、MFT、Copilot、推荐分析、聊天、诊断/自动化等关键路径日志。
- 已修复迁移过程中的编译问题并通过 Debug build。
- 已确认旧日志 API 在 `src` 下无残留。
- 已更新 codegraph 索引。
- 已发布 Release exe 到 `outputs/logging-overhaul-2026-06-29-1612`。
- 最终 `dotnet build src/SpaceMonger.sln -c Debug` 通过。
- 修复控制台 tab 崩溃：`Run.Text` 绑定只读属性时显式设置 `Mode=OneWay`。
