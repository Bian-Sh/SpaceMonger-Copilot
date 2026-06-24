# 设置页左右滚动布局迭代 - 2026-06-24-071542

## 变更内容
- 将设置页改为左侧设置导航、右侧单一连续 ScrollViewer 的布局。
- 左侧导航点击后会根据右侧内容区标题锚点执行 snap 跳转。
- 右侧滚动跨越不同设置区块时，左侧对应导航按钮会同步点亮。
- 保留原有设置项、绑定、自动保存、主题预设点击反馈与关闭按钮逻辑。
- 将设置面板和设置卡片圆角统一收敛到 10px，避免原 14px/20px 的过大视觉圆角。

## 验证
- 已执行 dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj，结果通过；仅存在既有 NuGet/nullable/resource 警告。
- 已执行 dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o outputs/settings-scroll-layout-2026-06-24-071542。

## 输出
- 发布目录：outputs/settings-scroll-layout-2026-06-24-071542
