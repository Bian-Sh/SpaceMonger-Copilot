# 迭代纪要：面包屑"此电脑"语言切换不更新修复 — 2026-06-19

## 问题
切换到中文后，面包屑仍显示 "This PC" 而非 "此电脑"。

## 根因
面包屑是通过 `RebuildBreadcrumbBar()` 命令式构建的，按钮文本来自 `ParsePathSegments()` 返回的 `(path, name)` 元组。即使 `ThisPC` 属性正确调用 `L.Text("ThisPCLabel")`，文本值在构建时就被"固化"到 UI 元素中——没有 WPF binding，语言切换时不会自动更新。

之前的修复只改了 `ThisPC` 从 `const` → `L.Text()`，但没有在语言切换时触发面包屑重建。

## 修复
`MainWindow.xaml.cs`：
- 构造函数中订阅 `L.LanguageChanged += OnAppLanguageChanged`
- `OnAppLanguageChanged()` 中通过 `Dispatcher.InvokeAsync` 调用 `RebuildBreadcrumbBar()`，确保在 UI 线程重建面包屑

## 改动文件
- `src/SpaceMonger.App/MainWindow.xaml.cs`：添加语言切换事件订阅与面包屑重建

## 打包
- `outputs\SpaceMonger-win-x64-folder-20260619-161057\SpaceMonger.App.exe`