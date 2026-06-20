# MainWindow 清理职责拆分纪要

时间：2026-06-20 13:22:04

## 本次变更

- 删除 MainWindow.ModalsAndCleanup.cs，不再用混合命名承载清理和模态逻辑。
- 新增 MainWindow.Cleanup.cs，仅保留清理请求、确认、执行、摘要和推荐列表更新流程。
- 新增 MainWindow.Modals.cs，集中放置 ShowAppMessageAsync、ShowAppModalAsync、ShowAppContentAsync 等通用模态 helper。
- 新增 MainWindow.Shell.cs，迁移设置页打开/关闭、聊天面板折叠等窗口壳层交互。
- 保持通用 AppModalHost 组件、关闭确认弹窗、分析无扫描提示和清理确认调用不变。

## 验证

- 已运行 dotnet build src/SpaceMonger.sln，构建通过；仍存在项目原有 NU1701 包兼容警告与重复资源键警告。
- 已运行 dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o outputs/mainwindow-cleanup-split-2026-06-20-132156。
- 已运行 npx @colbymchenry/codegraph sync 更新代码索引。

## 输出

- 发布目录：outputs/mainwindow-cleanup-split-2026-06-20-132156
