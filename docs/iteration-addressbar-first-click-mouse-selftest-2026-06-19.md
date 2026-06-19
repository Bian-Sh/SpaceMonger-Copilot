# 迭代纪要：地址栏编辑态退出后首击面包屑修复（2026-06-19）

## 背景

用户反馈：在地址栏进入编辑模式后，点击标题栏、聊天区、treemap 或底部区域退出编辑模式，再首次点击父级面包屑时，经常没有响应；用户要求必须用 Computer Use / 鼠标级真实交互自测。

## 修复

- 将地址栏编辑态的真实状态统一为 `PathEditTextBox.Visibility == Visible`。
- `LostFocus` 只调用幂等的 `ExitPathEditMode()`，非编辑态不会重复重建面包屑。
- 移除 `_justExitedEditMode`、`_suppressNextEditMode`、tick guard 等历史绕路状态，避免状态残留吞掉下一次点击。
- `Window_PreviewMouseLeftButtonDown` 在编辑态下点击输入框/浏览按钮外部时只负责退出编辑态并清除焦点，不再阻止目标区域后续真实点击。
- 保留 acceptance TCP 诊断入口，仅通过环境变量启用，用于准备长路径与读取状态；鼠标点击验证使用 Computer Use / Win32 真实点击。

## 自测

测试路径：`C:\tmp\sm-test\LongParent\MiddleFolder\LeafFolder`。

先使用 acceptance server 注入长路径与完成小目录扫描，然后使用真实鼠标点击进入编辑态、点击不同区域退出编辑态、首次点击 `MiddleFolder` 面包屑。

Computer Use 鼠标级矩阵结果：

| 退出区域 | 进入编辑态后 | 退出后 | 首次点击 MiddleFolder 后路径 | 结果 |
| --- | --- | --- | --- | --- |
| Chat | edit | breadcrumb | `C:\tmp\sm-test\LongParent\MiddleFolder` | 通过 |
| TitleLeft | edit | breadcrumb | `C:\tmp\sm-test\LongParent\MiddleFolder` | 通过 |
| TitleCenter | edit | breadcrumb | `C:\tmp\sm-test\LongParent\MiddleFolder` | 通过 |
| Treemap | edit | breadcrumb | `C:\tmp\sm-test\LongParent\MiddleFolder` | 通过 |
| BottomPanel | edit | breadcrumb | `C:\tmp\sm-test\LongParent\MiddleFolder` | 通过 |

同时使用 Win32 `SetCursorPos + mouse_event` 真实鼠标矩阵复测，同样全部通过。

## 构建与打包

- `dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj`：通过，仅有既有 `NU1701` 兼容性警告。
- Release folder publish：`outputs\SpaceMonger-win-x64-folder-20260619-134557`
- 启动程序：`outputs\SpaceMonger-win-x64-folder-20260619-134557\SpaceMonger.App.exe`

注意：这是 folder publish，需要整个目录一起分发，不要只复制单个 exe。
