# 2026-06-19 发布目录格式统一

## 变更

- 清空工程根目录 `publish` 下的历史发布内容。
- 改为统一发布到 `outputs` 目录。
- 本次发布目录命名沿用既有格式：`SpaceMonger-win-x64-folder-YYYYMMDD-HHMMSS`。

## 本次产物

- `outputs\SpaceMonger-win-x64-folder-20260619-164546`
- `outputs\SpaceMonger-win-x64-folder-20260619-164546\SpaceMonger.App.exe`

## 验证

- `dotnet publish src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o outputs\SpaceMonger-win-x64-folder-20260619-164546` 成功。
- `publish` 目录已清空。
