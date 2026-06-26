# 设置页面拆分重构迭代纪要（2026-06-26-092701）

## 背景

设置页面可配置板块持续增加，原 SettingsPage.xaml 同时承载导航、弹层框架、API 设置、通用设置、主题设置和共享样式，单文件体积偏大，后续维护成本较高。

## 本次调整

- 将设置页主体弹层、侧边导航、保存 toast 保留在 SettingsPage。
- 新增 Views/SettingsSections/ApiSettingsSection，承载 API Key、Base URL、模型名与 thinking 开关。
- 新增 Views/SettingsSections/GeneralSettingsSection，承载语言与删除模式设置。
- 新增 Views/SettingsSections/ThemeSettingsSection，承载主题预设、颜色、玻璃效果、背景类型、模糊和透明度等主题设置。
- 新增 Views/SettingsSections/SettingsSharedResources.xaml，集中复用设置项 label 样式、主题预设卡片样式和颜色转换器。
- 父页改为通过子控件锚点滚动与导航激活，子控件通过 SettingsChanged 事件回传需要保存的变更。

## 验证

- 已运行 dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj，构建通过。
- 构建仍存在项目既有警告：OpenTK/SkiaSharp WPF 包目标框架兼容性、少量 nullable 警告、重复资源名警告；本次未改动这些无关问题。
