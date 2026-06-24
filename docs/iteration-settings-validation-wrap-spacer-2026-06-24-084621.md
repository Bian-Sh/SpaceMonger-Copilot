# 设置页验证换行与 spacer 精算 - 2026-06-24-084621

## 变更内容
- 按 git 历史方向把 API Key 验证结果移到按钮下一行，不再挤在按钮右侧。
- 验证结果无状态时折叠，不占用高度；验证中、有效、无效时显示并参与布局测量。
- 修复 LocExtension 的语言刷新逻辑：只触发 Binding 的 PropertyChanged，不再手动 SetValue 覆盖绑定，避免设置页以外控件无法实时切换。
- BottomScrollSpacer 初始高度改为 0，并根据 Theme 标题偏移、实际内容高度、当前 ScrollViewer 视口高度动态计算。
- SettingsContentPanel 尺寸变化时会重新计算 spacer，因此验证结果换行出现/消失也会纳入计算。

## 验证
- 已执行 dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj，结果通过；仅有既有 NuGet/nullable 警告。
- 已执行 dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o outputs/settings-validation-wrap-spacer-2026-06-24-084621。

## 输出
- 发布目录：outputs/settings-validation-wrap-spacer-2026-06-24-084621
