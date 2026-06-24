# 主题框架实现 — 2026-06-23

## 概述
为 SpaceMonger Next 构建了完整的 WPF 动态主题框架，支持：
- 预设主题切换（Vision Pro Dark / Light / Frosted Glass）
- 自定义强调色（十六进制颜色输入）
- 磨砂玻璃效果（DWM Acrylic/Mica 背景）
- 高斯模糊（WPF BlurEffect，半径可调 0-60）
- 透明度控制（面板不透明度 0.1-1.0）
- 自动文字撞色（WCAG 亮度计算，自动匹配前景色）
- 入口：设置页 → 主题区域

## 新增文件

| 文件 | 说明 |
|------|------|
| `src/SpaceMonger.Core/Models/Theme/ThemeProfile.cs` | 主题数据模型，包含 3 个内置预设 |
| `src/SpaceMonger.App/Services/ThemeManager.cs` | 主题管理服务，运行时注入资源字典 |
| `src/SpaceMonger.App/Converters/HexColorConverter.cs` | 十六进制颜色 ↔ WPF Color 绑定转换器 |

## 修改文件

| 文件 | 变更 |
|------|------|
| `src/SpaceMonger.Core/Models/AppSettings.cs` | 增加 `ThemeProfile?` 持久化字段 |
| `src/SpaceMonger.App/ViewModels/SettingsViewModel.cs` | 增加主题相关属性、预设/自定义应用命令 |
| `src/SpaceMonger.App/Views/SettingsPage.xaml` | 增加主题设置区域（预设卡片、强调色、磨砂、模糊、透明度、撞色） |
| `src/SpaceMonger.App/Views/SettingsPage.xaml.cs` | 增加 `ThemePreset_Click` 预设卡片点击动画 |
| `src/SpaceMonger.App/App.xaml.cs` | DI 注册 ThemeManager，启动时初始化主题 |
| `src/SpaceMonger.App/Localization/Strings.resx` | 新增 17 条主题英文文案 |
| `src/SpaceMonger.App/Localization/Strings.zh-CN.resx` | 新增 17 条主题中文文案 |

## 架构设计

```
ThemeProfile (数据模型)
    ├── 颜色 (Accent, Background, Text...)
    ├── 磨砂 (GlassEnabled, GlassBackdropType)
    ├── 模糊 (BlurRadius 0-60)
    ├── 透明度 (GlassOpacity 0.1-1.0)
    └── 撞色 (AutoTextContrast)

ThemeManager (单例服务)
    ├── Initialize() → 从 AppSettings 加载主题
    ├── ApplyTheme(profile) → 运行时替换 ResourceDictionary
    ├── ApplyGlassBackdrop() → 调用 AcrylicHelper DWM API
    ├── CreateBlurEffect() → 返回 WPF Gaussian BlurEffect
    └── GetContrastTextColor() → WCAG 亮度计算
```

## 运行时行为
- 选择预设：即时应用全部颜色、模糊、透明度
- 自定义颜色：输入 hex 值后点「应用」
- 所有设置自动持久化到 `settings.dat`
- 磨砂效果仅在 Windows 11 22H2+ 生效，旧版静默降级

## 已知限制
- 磨砂 DWM 背景为窗口级别，仅支持主窗口
- WPF BlurEffect 应用于控件级，非真正背景模糊
- ComboBox 的 ComboBoxItem 字符串绑定需运行时转换（当前用 TextBlock item）
