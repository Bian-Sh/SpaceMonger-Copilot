# 工程偏好：修改后自动提供 exe（2026-06-16）

## 偏好

- 用户已明确：后续在本工程发生代码或 UI 修改后，不需要用户再单独说“打包 exe”，默认应主动提供新版本 exe。

## 默认打包方式

```powershell
dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-win-x64-folder-<timestamp>
```

## 交付要点

- 输出新的发布目录和 `SpaceMonger.App.exe` 路径。
- 提醒 folder publish 需要整个目录一起分发，不要只复制单个 exe。
- 保留每次打包纪要到 `docs/`。
