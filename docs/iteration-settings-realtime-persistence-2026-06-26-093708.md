# 设置实时保存与关闭无兜底保存迭代纪要（2026-06-26-093708）

## 背景

设置页拆分后，原先仍保留了由 UI 事件触发的 SavePendingChanges()，并且返回按钮会无差别保存当前设置。需求调整为：不在关闭/返回面板时执行兜底保存；设置修改应实时生效并实时落盘。

## 本次调整

- 移除设置页返回按钮中的无差别保存动作，关闭设置面板只负责关闭面板。
- 移除设置子控件 XAML 中的 LostFocus、SelectionChanged、Click 自动保存事件，避免 UI 层分散持久化逻辑。
- 在 SettingsViewModel 中为 API Key、Base URL、分析模型、聊天模型、thinking、语言、删除模式、API Key 验证状态增加属性变更持久化。
- 主题相关设置继续通过 ThemeManager.Persist() 即时落盘；主题预设改为应用后立即持久化一次，并用加载保护避免重复保存。
- 主窗口改为订阅 SettingsViewModel.SettingsChanged，设置变更后刷新语言和 API Key 状态。
- 保留旧 SettingsDialog 的显式 SaveCommand 兼容路径；这属于用户点击保存，不是关闭面板兜底保存。

## 实时生效说明

- 语言、主题、玻璃效果等 UI 设置会立即应用并落盘。
- API Key、Base URL、模型名、thinking、删除模式会立即落盘；对已经发起中的 LLM 请求/扫描流程不强行中断，通常从下一次相关操作开始使用新设置。
- API Key 清空会立即清除已保存密钥并重置验证状态；验证按钮完成后会实时保存验证结果。

## 验证

- 已运行 dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj，构建通过。
- 构建仍存在项目既有 warning：OpenTK/SkiaSharp WPF 包目标框架兼容性、少量 nullable 警告；本次未处理这些无关问题。
