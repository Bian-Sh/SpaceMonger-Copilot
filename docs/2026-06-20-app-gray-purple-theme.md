# 2026-06-20 APP 灰紫主题配色调整

## 背景

用户希望将 SpaceMonger Next 的 APP 颜色调整为截图所示的灰紫暗色风格；截图中央模态窗口被遮挡，本次不处理模态内容；文字颜色暂不调整。

## 修改内容

- 更新 `src/SpaceMonger.App/Themes/VisionProTheme.xaml` 中的全局主题 token。
- 主窗口背景从近黑色调整为截图主底色 `#343440`。
- 面板底色调整为 `#484853`，配合半透明 surface token 形成推荐区、聊天区、状态栏的层次。
- 按钮强调色调整为更接近截图的克制蓝色 `#2562A7`，hover 使用 `#2E73C2`。
- 遮罩色同步为灰紫调 `#80484853`，不涉及模态布局或文字配色。

## 验证

- 执行 `dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o publish/SpaceMonger.App` 成功。
- 发布过程中保留既有警告：`NU1701` 包兼容性警告，以及 `Strings.resx` 重复资源名 `NoItemsSelectedForCleanupToolTip` 警告。
- 使用 Computer Use 启动 `publish/SpaceMonger.App/SpaceMonger.App.exe` 并截图自测，主背景、右侧聊天面板、底部推荐区、输入框和蓝色按钮已与参考图的灰紫色层次匹配。
