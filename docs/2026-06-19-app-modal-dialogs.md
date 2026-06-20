# 2026-06-19 应用内模态弹窗改造

## 背景

将应用中原本依赖系统级 `MessageBox` 与独立 `Window.ShowDialog()` 的模态提示，改为主窗口内遮罩承载的 APP 内模态弹窗，避免弹窗脱离应用视觉层级。

## 主要改动

- 新增 `AppModalHost`，作为主窗口内统一模态宿主，支持消息弹窗与自定义内容弹窗。
- 在 `MainWindow` 顶层增加 `ModalHost` 遮罩层，优先级高于设置页 overlay。
- 将分析提示、API Key 提示、重新分析确认、清理错误/空选择提示从 `MessageBox.Show` 迁移到 APP 内模态。
- 将清理确认与清理结果总结从独立 `Window` 改为可嵌入 `UserControl`，由 `ModalHost` 承载。
- 将 Treemap 右键菜单中的“打开资源管理器失败”“复制路径失败”“属性”弹窗迁移到 APP 内模态。

## 验证

- 已执行 `dotnet build src\SpaceMonger.App\SpaceMonger.App.csproj -c Release`，构建成功。
- 已执行 `dotnet publish src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64`，发布成功。
- 构建仍存在既有警告：`OpenTK`/`OpenTK.GLWpfControl`/`SkiaSharp.Views.WPF` 兼容性警告，以及 `Strings.resx` 中重复资源名 `NoItemsSelectedForCleanupToolTip`。

## 产物

- 发布目录：`publish\win-x64`
- 可执行文件：`publish\win-x64\SpaceMonger.App.exe`
