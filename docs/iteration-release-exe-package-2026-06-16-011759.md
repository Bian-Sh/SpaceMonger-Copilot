# 迭代纪要：Release exe 打包（2026-06-16 01-17）

## 产物

- 发布目录：`outputs\SpaceMonger-win-x64-folder-20260616-011759`
- 启动程序：`outputs\SpaceMonger-win-x64-folder-20260616-011759\SpaceMonger.App.exe`
- 目录共 `489` 个文件，总大小约 `270 MB`。
- folder publish 需要整个目录一起分发，不要只复制单个 exe。

## 命令

```powershell
dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-win-x64-folder-20260616-011759
```
