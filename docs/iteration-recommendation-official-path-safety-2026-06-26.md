# 迭代纪要：推荐清理系统路径策略改为官方依据硬过滤（2026-06-26）

## 背景

用户指出上一版仍然以 `Windows\assembly`、`.NET Framework` 等具体缓存路径做白名单，虽然解决了 Visual Studio/.NET 缓存案例，但对未来新 App 在系统目录下写缓存的场景仍然会误伤。新的目标是：只依据 Windows 官方明确高风险/受保护的系统结构做硬过滤；其他系统相邻路径允许推荐，但安全等级降级，方便用户“询问/复核”。

## 查证依据

- Microsoft Learn：Windows Resource Protection 会保护操作系统安装的关键系统文件、文件夹和注册表键；应用不应修改这些受保护资源。
  - https://learn.microsoft.com/en-us/windows/win32/wfp/about-windows-file-protection
- Microsoft Learn：WinSxS 不能手动删除，删除 WinSxS 文件或整个目录可能导致系统无法启动或无法更新，应通过 DISM/任务计划/Disk Cleanup 等系统机制清理。
  - https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/clean-up-the-winsxs-folder?view=windows-11
- Microsoft Learn：`C:\Windows\Installer` 是 Windows Installer cache，存放卸载/更新应用所需的重要文件，不应删除。
  - https://learn.microsoft.com/en-us/troubleshoot/windows-client/application-management/missing-windows-installer-cache
- Microsoft Learn：Desktop/Documents/Pictures/Music/Videos 等属于用户 Known Folder，默认位于 `%USERPROFILE%` 下，应继续作为用户资料保护范围。
  - https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid

## 修改内容

- 去掉具体 app/cache 白名单：不再特殊识别 `Windows\assembly\temp`、`NativeImages_*`、`.NET Framework` 等缓存路径。
- 新增 `PathSafetyKind` 分类：
  - `HardProtected`：全盘分析中隐藏。
  - `RiskAdjusted`：允许进入推荐列表，但 `Safe` 降为 `ReviewFirst`。
  - `Normal`：按 AI 返回的安全等级展示。
- `Windows` 目录策略改为：
  - `Windows\System32`、`Windows\SysWOW64`、`Windows\WinSxS`、`Windows\servicing`、`Windows\Installer`、`Windows\SystemResources`、`Windows\Boot` 作为硬保护。
  - 其他 `Windows\...` 子路径不再硬过滤，统一视为 `RiskAdjusted`。
- `Program Files`、`Program Files (x86)`、用户 Known Folder（Desktop/Documents/Pictures/Music/Videos）继续硬保护。
- 预留扩展入口：`AdditionalHardProtectedPathPrefixes`、`AdditionalRiskAdjustedPathPrefixes`，后续可替换为设置、策略文件或 provider 注入。

## 关键文件

- `src/SpaceMonger.Core/Services/Analysis/RecommendationEngine.PathSafety.cs`
  - 新增路径分类策略和扩展入口。
  - 删除 app/cache 专项白名单。
- `src/SpaceMonger.Core/Services/Analysis/RecommendationEngine.cs`
  - 全盘分析只过滤 `IsHardProtectedPath()`。
  - 保留系统相邻路径风险降级。
- `tests/SpaceMonger.Core.Tests/RecommendationEngineTests.cs`
  - `C:\Windows\assembly\temp` 可展示但降级。
  - `C:\Windows\VendorApp\Cache` 这类未知 Windows 子目录可展示但降级。
  - `C:\Windows\System32\kernel32.dll` 仍硬过滤。

## 验证

- `npx @colbymchenry/codegraph sync`：完成索引同步。
- `dotnet test .\tests\SpaceMonger.Core.Tests\SpaceMonger.Core.Tests.csproj --filter RecommendationEngineTests`：通过，5/5。
- `dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-win-x64-folder-20260626-091612`：发布成功。

## 发布产物

- `outputs\SpaceMonger-win-x64-folder-20260626-091612\SpaceMonger.App.exe`

## 注意

发布过程中仍有项目既有 warning：OpenTK/SkiaSharp 包目标框架兼容性、两个 nullable 警告、`NoItemsSelectedForCleanupToolTip` 重复资源名。本次未处理这些无关问题。
