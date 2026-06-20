# 2026-06-20 设置面板重新打开时保存提示修复

## 背景

- 参考 `docs/iteration-settings-save-toast-fix-2026-06-19.md`，设置面板打开时不应因为初始化绑定或旧状态再次显示“设置已保存”提示。
- 当前设置面板为复用实例，`_isLoaded` 首次置 true 后，再次打开时 `LoadSettings()` 可能触发 `SelectionChanged` / `Click` 等自动保存事件；如果上次保存 toast 尚未消失，重新打开也会看到旧提示。

## 修改

- `src/SpaceMonger.App/Views/SettingsPage.xaml.cs`
  - 新增 `_suppressAutoSave` 标记。
  - 新增 `ReloadSettingsForOpen()`，打开设置页前停止 toast timer、隐藏旧 toast、重新加载设置，并暂时屏蔽本次加载产生的自动保存事件。
  - `AutoSaveOnChanged` / `AutoSaveOnLostFocus` 增加 `_suppressAutoSave` 判断。
- `src/SpaceMonger.App/MainWindow.ModalsAndCleanup.cs`
  - `ShowSettingsPage()` 改为调用 `SettingsPage.ReloadSettingsForOpen()`。

## 验证

- 已运行 `dotnet build src\SpaceMonger.sln --no-restore`。
- 已发布 `win-x64` folder 版到 `outputs\SpaceMonger-win-x64-folder-20260620-124316`，入口为 `SpaceMonger.App.exe`。

## 后续注意

- 设置页打开流程必须通过 `SettingsPage.ReloadSettingsForOpen()` 重新加载设置，不要在显示设置页时直接调用 `SettingsViewModel.LoadSettings()`。
- 新增设置控件并绑定 `SelectionChanged`、`Click`、`LostFocus` 等自动保存事件时，需要确认初始化加载不会触发 `SaveWithToast()`。
- 保存成功提示只应该由用户主动变更或关闭面板保存触发，重新打开设置页时应先隐藏旧 toast。
