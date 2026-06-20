# 迭代纪要：导航联动修复发布包（2026-06-18）

## 发布内容

本次发布包含 docs/iteration-folder-selection-navigation-sync-2026-06-18.md 记录的文件夹选择后导航联动修复。

## 发布命令

`powershell
dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-win-x64-folder-20260618-093336
Compress-Archive -LiteralPath outputs\SpaceMonger-win-x64-folder-20260618-093336 -DestinationPath outputs\SpaceMonger-win-x64-folder-20260618-093336.zip -Force
`

## 输出

- 发布目录：$out
- 压缩包：$zip
- 启动程序：$exe
- 压缩包大小：93.15 MB
- EXE 大小：0.15 MB

## 验证

- dotnet publish 成功。
- 仍存在既有 NU1701 包兼容 warning：OpenTK、OpenTK.GLWpfControl、SkiaSharp.Views.WPF。
