# 迭代纪要：Chat UX 改进（2026-06-20）

## 背景

用户要求改进聊天气泡的 UX：
1. 添加复制按钮（鼠标悬停时显示，使用 opencode 的图标）
2. 修复滚轮滚动问题
3. 气泡自适应窗口宽度

## 修改内容

### 1. 复制按钮

- 添加了 opencode 风格的复制图标（SVG path）
- 鼠标悬停在气泡上时显示复制按钮
- 点击复制按钮将消息文本复制到剪贴板
- 使用 `Opacity` 动画控制显示/隐藏

### 2. 滚轮滚动修复

- 添加了 `FlowDocumentScrollViewer_PreviewMouseWheel` 事件处理
- 当鼠标在聊天气泡上滚动时，事件会转发给父级 ScrollViewer
- 确保滚轮操作能正常滚动聊天列表

### 3. 气泡自适应宽度

- 移除了 `MaxWidth="320"` 限制
- 使用 `SubtractConverter` 计算最大宽度（窗口宽度 - 50px 边距）
- 气泡宽度会随窗口调整而变化

## 新增文件

- `src/SpaceMonger.App/Converters/SubtractConverter.cs` - 数值减法转换器

## 修改文件

- `src/SpaceMonger.App/Views/ChatPanel.xaml` - 重构消息模板
- `src/SpaceMonger.App/Views/ChatPanel.xaml.cs` - 添加事件处理
- `src/SpaceMonger.App/Localization/Strings.resx` - 添加 CopyMessageToolTip
- `src/SpaceMonger.App/Localization/Strings.zh-CN.resx` - 添加中文翻译

## 验证

- 已运行 `dotnet build src\SpaceMonger.sln`，构建成功
- 已发布 `win-x64` folder 版到 `outputs\SpaceMonger-chat-ux-20260620`

## 发布产物

- `outputs/SpaceMonger-chat-ux-20260620/SpaceMonger.App.exe`