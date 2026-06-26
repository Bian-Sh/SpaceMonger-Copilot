# 迭代纪要：推荐清理系统缓存路径安全策略细化（2026-06-26）

## 背景

此前推荐清理在全盘分析时将包含 `Windows`、`Program Files` 等路径的 AI 推荐全部过滤。用户指出这种一刀切策略会误伤 IDE、运行时、编译缓存等系统目录内的缓存，例如 `.NET Framework`/Native Images 相关缓存位于 `C:\Windows\assembly` 下，但并不等同于系统关键架构文件。

## 修改内容

- 将系统路径安全策略从单一 `IsProtectedPath()` 拆细为：
  - 硬保护路径：核心 OS 结构、`Program Files`、用户文档库等仍在全盘分析中隐藏。
  - 系统缓存候选：`Windows\Temp`、`Windows\Prefetch`、`Windows\assembly\temp`、`Windows\assembly\NativeImages_*`、`.NET Framework` cache 类路径允许进入推荐列表。
- 全盘分析不再把所有 `Windows` 路径无差别过滤，只过滤硬保护路径。
- 系统缓存候选即使 AI 标为 `Safe`，也会被降级为 `ReviewFirst`，并追加“System cache location - review carefully before deleting.”提示。
- 聚焦子路径分析继续保留风险降级语义，避免用户在系统/用户保护区域内看到无提示的 `Safe`。

## 关键文件

- `src/SpaceMonger.Core/Services/Analysis/RecommendationEngine.cs`
  - 全盘分析过滤改为只过滤 `IsHardProtectedPath()`。
  - 所有保留下来的推荐统一执行系统路径风险调整。
- `src/SpaceMonger.Core/Services/Analysis/RecommendationEngine.PathSafety.cs`
  - 新增系统缓存候选识别。
  - 保留硬保护路径识别。
  - 新增系统路径风险降级逻辑。
- `tests/SpaceMonger.Core.Tests/RecommendationEngineTests.cs`
  - 新增 `C:\Windows\assembly\temp` 不再被清空且降级为 `ReviewFirst` 的回归测试。
  - 新增 `C:\Windows\System32\kernel32.dll` 在全盘分析中仍被过滤的回归测试。

## 验证

- `dotnet test .\tests\SpaceMonger.Core.Tests\SpaceMonger.Core.Tests.csproj --filter RecommendationEngineTests`：通过，4/4。
- `dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-win-x64-folder-20260626-085610`：发布成功。

## 发布产物

- `outputs\SpaceMonger-win-x64-folder-20260626-085610\SpaceMonger.App.exe`

## 注意

发布过程中仍有项目既有警告：OpenTK/SkiaSharp 包目标框架兼容性、两个 nullable 警告、`NoItemsSelectedForCleanupToolTip` 重复资源名。本次未处理这些无关问题。

