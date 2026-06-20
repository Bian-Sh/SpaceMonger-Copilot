# 迭代纪要：Chat UX 改进（2026-06-20）

## 背景

用户要求改进聊天气泡的 UX：
1. 添加复制按钮（鼠标悬停时显示，使用 opencode 的图标）
2. 修复滚轮滚动问题
3. 气泡自适应窗口宽度
4. 用户消息复制按钮在右下角，AI消息复制按钮在左下角
5. 复制按钮放在气泡内部，划入后只显示icon，icon纯白，不要背景变色
6. 划入划出点击icon都要有响应的变化

## 修改内容

### 1. 复制按钮（opencode 风格，气泡内部，带交互反馈）

- 使用 opencode 的复制图标 SVG path：`M6.2513 6.24935V2.91602H17.0846V13.7493H13.7513M13.7513 6.24935V17.0827H2.91797V6.24935H13.7513Z`
- **用户消息**：复制按钮在气泡**右下角**（内部）
- **AI消息**：复制按钮在气泡**左下角**（内部）
- 点击复制按钮复制完整的 markdown 原始文本
- icon 纯白色 `Stroke="White"`，默认 `Opacity="0.6"`
- **交互反馈**：
  - 鼠标划入：icon 透明度 0.6→1（变亮）
  - 鼠标划出：icon 透明度 1→0.6（恢复）
  - 点击：icon 短暂变为浅蓝色（300ms），然后恢复白色
- 无背景色，无边框，纯透明
- 使用 Border + MouseLeftButtonDown 替代 Button，避免按钮样式干扰

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
- 已发布 `win-x64` folder 版到 `outputs\SpaceMonger-chat-copy-v5-20260620`

## 发布产物

- `outputs/SpaceMonger-chat-copy-v5-20260620/SpaceMonger.App.exe`