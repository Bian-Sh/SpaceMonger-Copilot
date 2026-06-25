# 迭代纪要：管理员权限清单与输出目录清理

- 日期：2026-06-25
- 目标：清空 `outputs`，并将 `src/SpaceMonger.App/app.manifest` 的启动级别调整为管理员授权。

## 本次变更

1. 清空了仓库根目录 `outputs/` 下的全部内容。
2. 将 `src/SpaceMonger.App/app.manifest` 中的 `requestedExecutionLevel` 从 `asInvoker` 改为 `requireAdministrator`。

## 说明

- 该调整会让应用启动时请求提权，适合需要更高权限访问磁盘相关能力的场景。
- 当前仅修改清单文件本身，未额外变更构建脚本或发布流程。

## 打包与运行

- 发布目录：`outputs/2026-06-25_090938/`，并已启动其中的 `SpaceMonger.App.exe`。
- 运行进程：PID `33012`。
- 进程路径校验：`D:\AppData\Visual Studio\Projects\spacemonger-next\outputs\2026-06-25_090938\SpaceMonger.App.exe`。
- 由于已启动的管理员权限进程锁定了首个发布目录，随后额外生成了未占用的发布包：`outputs/2026-06-25_091233/`。
- 压缩包：`outputs/spacemonger-2026-06-25_091233.zip`。
- 发布过程中存在既有警告：`OpenTK`、`OpenTK.GLWpfControl`、`SkiaSharp.Views.WPF` 的 `NU1701` 兼容性提示，以及若干 nullable/资源重复警告；本次未改动这些问题。
