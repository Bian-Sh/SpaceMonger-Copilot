# 发现记录

- 选择 Serilog：.NET 生态主流、结构化日志成熟、文件 sink 简洁，并能通过 Serilog.Extensions.Logging 接入现有 DI ILogger。
- 旧日志残留检查：`rg "Trace\.Write|Debug\.Write|File\.AppendAllText|AppendConsoleLine|ConsoleLogLevel|ConsoleLogEntry|ConsoleTextBox|_console" src -S` 无匹配。
- `dotnet build src/SpaceMonger.sln -c Debug` 已通过；仍有既有/邻近 warning，包括 OpenTK/SkiaSharp NU1701 和若干 nullable warning。
- `dotnet test src/SpaceMonger.sln -c Debug --no-build` 有 1 个失败：`RecommendationEngineTests.AnalyzeWithDiagnosticsAsync_AddsUnityLibraryRecommendationWhenProjectMarkersExist`，与本次日志迁移无直接关系；随后 App.Tests 测试宿主未退出，已终止本次 test 进程。
- Release publish 成功：`outputs/logging-overhaul-2026-06-29-1612/SpaceMonger.App.exe`。
- `git diff --check` 通过，仅剩 Git 的 LF→CRLF 提示。
- 控制台崩溃根因：WPF `Run.Text` 默认 TwoWay，绑定 `UiLogEntry.TimeText` 等只读属性时 DataTemplate 实例化抛 `XamlParseException`。
- 已用后台 UIA 点击 `控制台` tab 验证：控制台渲染成功，进程保持响应。
