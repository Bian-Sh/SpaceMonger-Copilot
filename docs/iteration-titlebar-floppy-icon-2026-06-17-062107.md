# 迭代纪要：标题栏软盘图标替换（2026-06-17 06-21）

## 背景

标题栏左侧当前 glyph 辨识度不足，视觉上像两个同心圆。用户要求改成软盘图标以提高辨识度。

## 改动

- `src/SpaceMonger.App/Controls/WindowTitleBar.xaml`
  - 将标题栏左侧图标从硬盘 glyph 改为软盘 glyph：`E74E`。
  - 保持白色显示。

## 验证

- `dotnet build src\SpaceMonger.sln -c Debug` 通过，`0` errors。
- 已发布新的 Windows x64 folder publish：
  - `outputs\SpaceMonger-win-x64-folder-20260617-062107`
  - `outputs\SpaceMonger-win-x64-folder-20260617-062107\SpaceMonger.App.exe`
- 仍存在既有 `NU1701` 兼容性警告。
