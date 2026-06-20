# 迭代纪要：Release exe 打包（2026-06-19 10-53）

## 背景

本次在修复地址栏编辑态退出后首次点击面包屑 `Local` 无响应的问题后，按工程偏好主动生成新的 Windows x64 Release folder publish。

## 命令

```powershell
dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-win-x64-folder-20260619-105304
```

## 产物

- 发布目录：`outputs\SpaceMonger-win-x64-folder-20260619-105304`
- 启动程序：`outputs\SpaceMonger-win-x64-folder-20260619-105304\SpaceMonger.App.exe`
- 这是 folder publish，需要整个目录一起分发，不要只复制单个 exe。

## 验证

- `dotnet test src\SpaceMonger.sln --no-restore --filter TreemapViewModelTests` 通过。
- `dotnet publish` 成功完成。
- 生成 `SpaceMonger.App.exe`，目标运行时为 `win-x64`，发布模式为 `Release`，`--self-contained true`。
- 发布期间仍出现既存 `NU1701` 包兼容 warning，涉及 `OpenTK`、`OpenTK.GLWpfControl`、`SkiaSharp.Views.WPF`，未阻断发布。