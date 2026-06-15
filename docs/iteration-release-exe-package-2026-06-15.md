# 迭代纪要：Release exe 打包（2026-06-15）

## 目标

- 为当前 WPF/.NET 8 项目生成可直接运行的 Windows x64 exe 发布产物。

## 执行命令

```powershell
dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-win-x64-folder-20260615-234553
```

## 产物

- 发布目录：`outputs\SpaceMonger-win-x64-folder-20260615-234553`
- 启动程序：`outputs\SpaceMonger-win-x64-folder-20260615-234553\SpaceMonger.App.exe`
- 本次采用 folder publish，保留同目录 DLL、runtime、资源文件和本地化目录；不要只复制单个 exe。

## 验证信号

- `dotnet publish` 成功完成。
- 生成 `SpaceMonger.App.exe`，目标运行时为 `win-x64`，发布模式为 `Release`，`--self-contained true`。

## 注意事项

- 发布过程中仍出现既有 `NU1701` 警告，涉及 `OpenTK 3.3.1`、`OpenTK.GLWpfControl 3.3.0`、`SkiaSharp.Views.WPF 3.119.2` 与 `net8.0-windows7.0` 兼容性提示；本次未处理该既有警告。
- 历史纪要显示单文件发布曾遇到 `GenerateBundle` 写入 exe 被占用，因此本次继续使用 folder publish。
- 应用 manifest 使用 `requireAdministrator` 时，双击或运行可能触发 UAC 提权。
