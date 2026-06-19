# 设置面板保存逻辑与本地化修复（2026-06-19）

## 背景

设置面板内 CheckBox / ComboBox 选择后应立即保存生效（用户偏好），但初始化时数据绑定触发的 SelectionChanged/Click 不应保存（避免面板刚打开就弹出 toast）。

另外 `SettingsSavedToast` 本地化 key 缺失，导致 Toast 显示 `!SettingsSavedToast!`。

## 修改内容

### `src/SpaceMonger.App/Views/SettingsPage.xaml.cs`
- 添加 `_isLoaded` 标记，在 `Loaded` 事件后通过 `Dispatcher.BeginInvoke(DispatcherPriority.Loaded)` 置 true
- `AutoSaveOnChanged` 和 `AutoSaveOnLostFocus` 仅在 `_isLoaded` 为 true 时执行保存
- `BackButton_Click`（面板关闭）始终保存，不受 `_isLoaded` 限制

### `src/SpaceMonger.App/Views/SettingsPage.xaml`
- 保留所有原有事件绑定：4 处 `LostFocus="AutoSaveOnLostFocus"`、1 处 `Click="AutoSaveOnChanged"`、2 处 `SelectionChanged="AutoSaveOnChanged"`

### `src/SpaceMonger.App/Localization/Strings.resx`
- 添加 `SettingsSavedToast = Settings saved`

### `src/SpaceMonger.App/Localization/Strings.zh-CN.resx`
- 添加 `SettingsSavedToast = 设置已保存`

## 验证

- `dotnet build SpaceMonger.sln --no-restore`：通过，0 errors
- `dotnet publish` win-x64 Release：通过

## 发布输出

- `outputs/SpaceMonger-win-x64-folder-20260619-152414/SpaceMonger.App.exe`