# 通用模态弹窗垂直布局微调纪要

时间：2026-06-20 13:30:16

## 本次微调

- 按反馈将通用弹窗 title 上移 8px：标题顶部 margin 从 26 调整为 18。
- 按反馈将按钮组下移 8px：按钮组顶部 margin 从 2 调整为 10。
- 保持面板高度、content 居中组、左侧圆环 icon 和通用调用链不变。

## 验证

- 已运行 dotnet build src/SpaceMonger.sln，构建通过；仍存在项目原有 NU1701 包兼容警告与重复资源键警告。
- 已运行 dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o outputs/modal-vertical-balance-2026-06-20-133009。
- 已运行 npx @colbymchenry/codegraph sync 更新代码索引。

## 输出

- 发布目录：outputs/modal-vertical-balance-2026-06-20-133009
