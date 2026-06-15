# 迭代纪要：Release exe 打包（2026-06-16 00-49）

## 目标

- 在顶部取消按钮、控制台筛选菜单和设置按钮样式调整后，生成新的 Windows x64 Release exe 发布产物。

## 执行命令

```powershell
dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-win-x64-folder-20260616-004955
```

## 产物

- 发布目录：`outputs\SpaceMonger-win-x64-folder-20260616-004955`
- 启动程序：`outputs\SpaceMonger-win-x64-folder-20260616-004955\SpaceMonger.App.exe`
- 目录共 `489` 个文件，总大小约 `270 MB`。
- 仍是 folder publish，需要整个目录一起分发，不要只复制单个 exe。

## 验证信号

- `dotnet publish` 成功完成。
- 生成 `SpaceMonger.App.exe`，目标运行时为 `win-x64`，发布模式为 `Release`，`--self-contained true`。

## 注意事项

- 发布过程仍出现既有 `NU1701` 包兼容警告，本次未处理。
- 应用 manifest 使用 `requireAdministrator` 时，双击或运行可能触发 UAC 提权。
