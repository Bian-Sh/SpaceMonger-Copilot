# 迭代纪要：截图反馈二次修正（2026-06-20）

## 背景

根据用户新截图与五条反馈，修正上一轮 UI 调整中的误改和回归。

## 修改内容

- 回退左侧工具栏后退/前进/上级三个箭头的放大与白色化，避免误处理。
- 放大地址栏内部面包屑分隔符 `›`，提升输入框内箭头的可见性。
- 修复控制台切换后无数据显示的问题，恢复以 `ConsoleTextBox.Visibility` 控制显示。
- 移除底部状态栏中的扫描/日志跳转输出块，不再提供点击跳控制台的状态栏入口。
- 移除未跟踪的 `AppModalHost` 新宿主，改回普通窗口/消息框路径，避免破坏原有弹窗内容样式。
- 调整 treemap 线条到更细、更轻的描边，并回退过大的文字字号，降低块线条突兀感。

## 验证

- `dotnet build src/SpaceMonger.sln -c Release`：通过，0 错误；保留既有 `NU1701` 兼容性警告。
- `dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o outputs\spacemonger-next-ui-feedback-fix-20260620-104405`：通过。

## 输出

- 发布目录：`outputs\spacemonger-next-ui-feedback-fix-20260620-104405`

## 电脑自测补充

- 已通过 computer-use 启动 `outputs\spacemonger-next-ui-feedback-fix-20260620-103831` 并截图确认主界面可打开、控制台 Tab 可切换。
- 自测发现推荐空态仍提示“从状态栏打开控制台”，已改为“切换到控制台查看分析过程”，并重新发布到 `outputs\spacemonger-next-ui-feedback-fix-20260620-104405`。

## 最终自测结果

- computer-use 已恢复可用，成功连接 Windows 控制。
- 使用 `dotnet outputs\spacemonger-next-ui-feedback-fix-20260620-104405\SpaceMonger.App.dll` 启动最终包并截图确认：主界面打开正常。
- 确认推荐空态文案已变为“可以切换到控制台查看分析过程，或调整筛选条件后重试。”
- 点击控制台 Tab 后 `ConsoleTextBox` 可见，底部状态栏不再包含 `ScanProgressText` 日志入口。
