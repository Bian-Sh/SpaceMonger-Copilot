# 通用模态弹窗布局和谐化迭代纪要

时间：2026-06-20 13:35:20

## 本次变更

- 重构 CreateMessageView 布局：去掉了分三行的 Grid 行高分配（Auto/Star/Auto），改用单个居中 StackPanel 包裹标题、content、按钮组。
- 标题/content/按钮组现在作为一个流式整体垂直居中在卡片内，按钮组自动走到下方，content 不再悬浮在中间。
- 保持左侧圆环 icon、圆角、遮罩、阴影等视觉参数不变。
- 保持通用 ShowAsync API、关闭确认弹窗等调用链不变。

## 验证

- 已运行 dotnet build src/SpaceMonger.sln，构建通过；仍存在项目原有 NU1701 包兼容警告与重复资源键警告。
- 已运行 dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o outputs/modal-layout-harmony-2026-06-20-133512。
- 已运行 npx @colbymchenry/codegraph sync 更新代码索引。

## 输出

- 发布目录：outputs/modal-layout-harmony-2026-06-20-133512
